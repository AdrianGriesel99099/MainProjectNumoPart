# Working in this repo

ASP.NET Core 8 Razor Pages app tracking vehicle photos for a workshop — real customer vehicles,
real photos, a live production deployment. Read `docs/LOCAL_DEV.md` for setup/dev workflow and
`docs/OPERATIONS.md` for production infrastructure before assuming either.

## Testing

`dotnet build` then `dotnet test`. If Azurite (the blob storage emulator) isn't running,
`BlobPhotoStorageTests` will fail for that reason alone — exclude it rather than treating those
failures as real:

```bash
dotnet test --filter "FullyQualifiedName!~BlobPhotoStorageTests"
```

Every other test must pass. A green build is not the same as a green test run — run both.

## The `Part` enum (`Models/Part.cs`)

**Append-only. Never renumber, never reuse a freed number.** EF Core persists this enum as `int`,
so the number IS the database value — inserting a member in the middle silently re-labels every
existing photo. New members get the next free number, added at the bottom. `PartTests.
NumericValuesNeverChange` pins every existing value; if it fails, something renumbered instead of
appending.

## Database migrations

There is **no automated migration-apply step anywhere in this pipeline** — not in the Dockerfile,
not in `Program.cs`, not in CI. `dotnet ef database update` only runs against a developer's local
SQLite database. A migration that needs to reach production has to be applied there manually and
deliberately, outside of any automated deploy.

This matters for anything that adds a migration: **do not assume a deploy will apply it.** If your
change includes a new EF Core migration, say so explicitly and loudly in the PR description (e.g. a
`MIGRATION NEEDED:` line at the top) so it gets a human's attention rather than silently shipping
code that expects a schema the database doesn't have yet.

**Two migration histories, one gotcha.** This app keeps parallel migration sets — `Migrations/`
(SQLite, scaffolded normally with `dotnet ef migrations add`) and `Migrations/SqlServer/`
(hand-written, mirroring the SQLite one column-for-column; see the comment atop any file in that
folder). Both register against the same `AppDbContext`, so EF's tooling does **not** treat them as
separate timelines — `dotnet ef database update` walks *every* discovered migration in timestamp
order regardless of provider, meaning a plain run against a fresh SQLite database dies partway
through with "table already exists" the moment it reaches the first SQL Server migration (confirmed
2026-09-09; `docs/LOCAL_DEV.md`'s documented setup steps don't work as written until this is fixed
properly). The working local fix: temporarily add `<Compile Remove="Migrations/SqlServer/**" />` to
`MainProjectNumoPart.csproj`, run `dotnet ef database update`, then revert it. For production, the
new migration was hand-translated to raw T-SQL (including the matching `__EFMigrationsHistory`
insert) and run directly against Azure SQL rather than fighting this through the CLI. A real fix —
most likely giving each provider's migrations its own assembly — is still open.

## Style

- No comments explaining *what* code does — names should already do that. A comment earns its place
  only for a non-obvious *why*: a hidden constraint, a workaround, something that would surprise a
  reader.
- Don't add abstractions, config options, or error handling for cases that can't happen here. Match
  the scope of the change to what's actually needed.
- Commit messages explain why, not what — the diff already shows what changed.

## Verification standard

Before calling anything done: build, run the full test suite, and where practical, actually exercise
the change (locally with the app running, or by checking the live behavior after a deploy) rather
than trusting that code which compiles does what it's meant to. Several real bugs this repo has
shipped were only caught by that last step — a passing test suite is necessary, not sufficient.

## Automated daily loops

Several routines (`docs/OPERATIONS.md` → "Automated daily loops") run unattended against this repo:
code review, feature-building, functionality improvements, a UX pass, and an evening deploy. Every
one of them **works on its own branch and opens a PR — none of them push to master directly.**
`docker-build.yml` deploys on every push to master, so a direct push from one of these would trigger
an uncoordinated, unreviewed production deploy outside the one evening slot meant to own that. Only
the evening deploy loop merges to master, and only after tests pass and after checking for
`MIGRATION NEEDED:` in open PRs from that day (those are skipped, left for manual handling).

If a production rollback is ever needed, see the `Rollback deploy` GitHub Actions workflow
(`.github/workflows/rollback.yml`, manually triggered) rather than reasoning it out from scratch.

The feature-building routine isn't fully free-form any more: it checks `docs/BACKLOG.md` first,
which the "Feature proposals & review" section below can populate with something a human actually
approved — it only falls back to inventing its own idea when that queue is empty.

## Changelog (`Data/changelog.json` → `/Changelog` page)

User-facing "what's new" entries, read at runtime from `Data/changelog.json` by
`Pages/Changelog.cshtml.cs`. Each entry is `{date, title, description, link?}`; `description` is
shown to end users, so write it in plain language, not implementation detail. `link` is optional —
when a shipped feature traces back to an approved proposal (see "Feature proposals & review"
below), it points back at `/Features/<id>`. The evening deploy loop appends an entry here when it
merges that day's `auto/feature-<date>` PR (see its own prompt) — don't hand-maintain this file for
feature work that loop already covers. The file is copied to the published output via an explicit
`<None>` entry in the `.csproj` (it isn't under `wwwroot/`, so this isn't automatic) — if the copy
directive is ever removed, the page silently renders empty in production rather than erroring.

## Feature proposals & review (`Pages/Features`, `FeatureProposal`/`FeatureProposalRound`)

A human-reviewed gate in front of the feature-building loop, so it builds something a person
actually asked for and approved rather than whatever it freely invents. Anyone Staff/Admin can
submit an idea at `/Features`; the state machine (`Services/FeatureProposalService.cs`,
`Models/FeatureProposalStatus.cs`) is: `NeedsReview` (awaiting a human decision, whether that's the
raw submission or a round Claude just revised) → human picks **Looks good — continue** / **Needs
changes** (comment required) / **Reject idea** → `AwaitingAiRevision` or `Denied` (terminal).
`.github/workflows/feature-review.yml` runs at 04:00 SAST, one hour before the feature-building
routine: it drafts a fuller write-up for anything `AwaitingAiRevision` via the Anthropic API
(`Tools/FeatureReviewBot`), landing back on `NeedsReview` or `ReadyForFinalApproval` depending on
whether Claude thinks it's fully specified. Only from `ReadyForFinalApproval` does a human's
**Approve — build this** actually mean final approval (`Approved`); accepting earlier than that
just sends it around for another round. The same nightly workflow also writes every `Approved`
proposal not yet queued into `docs/BACKLOG.md` as `Build: <title> (proposal #<id>): <description>`
— the *only* bridge back to the feature-building routine, since neither that routine nor the
evening deploy routine ever talks to this database directly (cloud Claude routines only ever see a
git checkout; see the "Two migration histories" note above for why this database can't be reached
from a routine directly). The evening deploy routine's own prompt looks for `(proposal #<id>)` in a
merged PR's title and, when present, sets that changelog entry's `link` to `/Features/<id>`.

`Tools/FeatureReviewBot` needs `ANTHROPIC_API_KEY` and `AZURE_FEATURE_BOT_CLIENT_ID` (a dedicated
App Registration, OIDC-federated the same way as the backup workflow's identity, holding a SQL
Server contained-user grant scoped to exactly `dbo.FeatureProposals` and
`dbo.FeatureProposalRounds` — no other table, no Storage access) as GitHub secrets. It connects via
`AZURE_SQL_CONNECTION_STRING`'s `Authentication=Active Directory Default`, which reuses the az-cli
session `azure/login` establishes in the workflow rather than storing a SQL credential anywhere.
