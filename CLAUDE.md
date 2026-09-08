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
