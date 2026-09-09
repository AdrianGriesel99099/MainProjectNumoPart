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
- **User-facing text never names Claude or AI** — not on a page, not in a changelog entry, not in a
  status badge. Describe what happens ("drafts a fuller write-up", "queued to build"), not what's
  doing it. This is about copy a workshop user reads, not code comments or docs like this file.

## Verification standard

Before calling anything done: build, run the full test suite, and where practical, actually exercise
the change (locally with the app running, or by checking the live behavior after a deploy) rather
than trusting that code which compiles does what it's meant to. Several real bugs this repo has
shipped were only caught by that last step — a passing test suite is necessary, not sufficient.

## Automated daily loops

Seven routines (`docs/OPERATIONS.md` → "Automated daily loops") run unattended against this repo:
code review, feature review, feature-building, functionality improvements, a UX pass, another code
review, and an evening deploy. Five of them **work on their own branch and open a PR — they never
push to master directly.** `docker-build.yml` deploys on every push to master, so a direct push
from one of these would trigger an uncoordinated, unreviewed production deploy outside the one
evening slot meant to own that. Only the evening deploy loop merges application-code PRs to master,
and only after tests pass and after checking for `MIGRATION NEEDED:` in open PRs from that day
(those are skipped, left for manual handling).

The feature review routine (04:00 SAST) is the one deliberate exception — it pushes a
`docs/BACKLOG.md` update directly to master, not through a PR. See "Feature proposals & review"
below for why: the queued item has to be on master before the 05:00 feature-building routine reads
it an hour later, and a docs-only line is a different risk category from shipping application code
outside the evening slot (the redundant deploy it triggers redeploys the exact same image, nothing
new). It never touches application code, tests, or anything else in the repo.

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
The "Feature review (04:00 SAST)" cloud routine runs one hour before the feature-building routine.
Being Claude itself, it doesn't call any external AI API to draft a revision — it just reasons
about the proposal directly and posts the result — so this is a *cloud routine*, not a GitHub
Actions workflow, the same as the other six. It curls the site's own `/api/bot/feature-proposals/*`
routes (`Endpoints/FeatureProposalEndpoints.cs`, gated by `ApiKeyEndpointFilter` — a shared secret
in `FeatureReviewBot:ApiKey`, checked against a header the routine sends on every request, since
there's no user for a routine to sign in as; the site rejects everything if that key isn't
configured, rather than falling open):

1. `GET /awaiting-ai-revision` — for each proposal, draft a fuller write-up addressing the human's
   last comment and decide whether it's now fully specified, then `POST /{id}/revision`. This lands
   the proposal on `NeedsReview` or `ReadyForFinalApproval` depending on that decision. Only from
   `ReadyForFinalApproval` does a human's **Approve — build this** actually mean final approval
   (`Approved`) — accepting earlier than that (`NeedsReview`) just sends it around for another round.
2. `GET /approved-unqueued` — for each, append a line to `docs/BACKLOG.md` as
   `Build: <title> (proposal #<id>): <description>` and `POST /{id}/mark-queued`, then commit and
   push that file directly to master (see "Automated daily loops" above for why this routine, alone
   among the seven, pushes directly rather than opening a PR).

This is the *only* bridge back to the feature-building routine — neither that routine nor the
evening deploy routine ever talks to this database directly (cloud Claude routines only ever see a
git checkout, plus whatever a routine's own prompt has it curl; see the "Two migration histories"
note above for why direct database access isn't an option here). The evening deploy routine's own
prompt looks for `(proposal #<id>)` in a merged PR's title and, when present, sets that changelog
entry's `link` to `/Features/<id>`.
