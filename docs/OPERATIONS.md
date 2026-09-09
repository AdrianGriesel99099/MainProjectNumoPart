# Operations

Production infrastructure notes that don't belong in `LOCAL_DEV.md` — that file is about running
this app on your own machine; this one is about the real, permanently-running Azure resources.

## Automated daily loops

Six cloud Claude Code routines run unattended against this repo (`claude.ai/code/routines`, not a
GitHub Actions workflow — they clone the repo but have no access to production Azure resources at
all, unlike the GitHub Actions workflows below). Every one of the first five works on its own
branch and opens a PR; **only the evening deploy routine merges to master**, and only after that
day's PRs pass tests and don't contain `MIGRATION NEEDED:` (see `CLAUDE.md` → "Database migrations").

| Time (SAST / UTC) | Routine | Branch | Does |
|---|---|---|---|
| 00:00 / 22:00 | Morning code review | `auto/codereview-morning-<date>` | Reviews the previous day's merged work, opens fix PRs for anything it finds |
| 05:00 / 03:00 | New feature | `auto/feature-<date>` | Builds the top `docs/BACKLOG.md` item if one's queued (see "Feature proposals & review" in `CLAUDE.md`), otherwise picks its own small feature |
| 10:00 / 08:00 | Functionality improvement | `auto/improvement-<date>` | Test-first improvement to something that already exists |
| 15:00 / 13:00 | Frontend/UX pass | `auto/ux-<date>` | Small, focused UI polish |
| 19:00 / 17:00 | Evening code review | `auto/codereview-evening-<date>` | Reviews that day's other four PRs, approves or requests changes |
| 20:00 / 18:00 | Deploy today's changes | *(merges to master directly)* | Merges every approved, migration-clean PR from that day one at a time, watching each deploy before merging the next; also appends the day's shipped features to `Data/changelog.json` |

Two GitHub Actions workflows run on their own schedules alongside these — "Photo backup" and
"Feature proposal review" below. Unlike the cloud routines, both can reach production directly:
the backup workflow via its own narrowly-scoped Azure OIDC identity, the review workflow via a
shared API key against the site's own API (see that section for why it doesn't use OIDC).

## Photo backup

`.github/workflows/backup-photos.yml` runs daily (02:00 UTC / 04:00 SAST) and copies every blob in
`stworkshopphotosprod`'s `originals` container to a second storage account,
`stworkshopphotosbkup`, same resource group and region. `workflow_dispatch` is also enabled, so it
can be run on demand from the Actions tab without waiting for the schedule.

**What it protects against, and what it doesn't.** This is an *additive* sync — `azcopy sync`
without `--delete-destination` — so a photo deleted from production (by mistake, by a bug, or by
someone with delete access acting in bad faith) stays recoverable in the backup account
indefinitely. It does **not** protect against a regional Azure outage or disaster: the backup
account is in the same region as production, a deliberate cost/complexity tradeoff for this app's
scale rather than an oversight. Thumbnails are **not** backed up — `Services/ThumbnailGenerator.cs`
regenerates them from an original in well under a second, so backing up a derived asset that cheap
to recreate would only be doubling storage cost for no real protection.

**Identity.** The workflow authenticates via OIDC as its own App Registration,
`github-backup-workshop-photos` (app id `35a91bac-f720-4d50-9765-e98c7b4c7093`) — deliberately
**not** the `github-deploy-workshop-photos` identity `docker-build.yml` uses. It holds exactly two
role assignments, both scoped to a single container rather than a whole storage account:

| Role | Scope |
|---|---|
| Storage Blob Data Reader | `stworkshopphotosprod` → `originals` container |
| Storage Blob Data Contributor | `stworkshopphotosbkup` → `originals` container |

It has no access to the Container App, no access to either account's `thumbnails` container, and
no access to anything outside those two role assignments. A compromised or misconfigured backup
credential can read production photos and write to the backup account — nothing else. Its
federated identity credential (`github-master-backup`) uses the same subject pattern as the
deploy identity's (`repo:AdrianGriesel99099/MainProjectNumoPart:ref:refs/heads/master` — Azure's
federated-credential subject matching only supports branch/environment/tag granularity, not
per-workflow-file, so the real isolation between the two jobs is the separate App Registrations and
their independently-scoped role assignments above, not the subject string).

`AZURE_BACKUP_CLIENT_ID` is a repo secret holding that app id. `AZURE_TENANT_ID` and
`AZURE_SUBSCRIPTION_ID` are shared with the deploy workflow — those two aren't identity-specific,
just tenant/subscription context, so there was no reason to duplicate them.

**Restoring a photo.** There is deliberately no automation for this direction — a restore should be
a deliberate action someone takes, not something that could ever run on a schedule. From the Azure
Portal (or `az storage blob copy` / `azcopy copy` with `az login`'d credentials that have read
access to the backup account and write access to production), copy the specific blob back from
`stworkshopphotosbkup`'s `originals` container to `stworkshopphotosprod`'s. The blob's path already
encodes what it is — `Services/PhotoNaming.cs`'s `{identifierPart}-{seq}{extension}` scheme under
`{VehicleBlobFolderName}/{Stage}/` — so the same path in the backup account is the same photo. The
database record for it needs to exist too (or be recreated) for the app to know the file is there;
this only restores the blob itself, not any `Photos` table row that referenced it.

**Checking it's actually working.** The Actions tab shows each run; a green run with a non-trivial
"Sync originals to backup storage account" step log (AzCopy prints a per-file transfer summary) is
the real signal, not just "the workflow exists." After first setting this up, or after any change
to `backup-photos.yml`, trigger it manually via `workflow_dispatch` and read the log rather than
waiting up to 24 hours for the schedule to prove it either way.

## Feature proposal review

`.github/workflows/feature-review.yml` runs daily (02:00 UTC / 04:00 SAST, one hour before the
"New feature" cloud routine above) and `workflow_dispatch`. It's the review/approval gate described
in `CLAUDE.md` → "Feature proposals & review": it drafts the next revision for every proposal a
human sent back for changes, and queues every proposal a human gave final approval to into
`docs/BACKLOG.md` for the feature-building routine to pick up.

**What it touches, and what it doesn't.** Unlike the other two workflows on this page, it has no
Azure credential at all and never talks to the database directly. `Tools/FeatureReviewBot` (a plain
HTTP console app, no database driver, no Azure SDK) calls the production site's own
`/api/bot/feature-proposals/*` routes — the same `FeatureProposalService` the web app itself uses,
so this workflow can never see or touch anything that service doesn't already expose. It also calls
the Anthropic API to draft each revision, and can push a single commit to `docs/BACKLOG.md`
(nothing else) when something gets queued.

**Authentication.** A shared secret, not OIDC — there's no Azure resource for this workflow to
authenticate to. `FEATURE_REVIEW_API_KEY` (repo secret) is sent as an `X-Api-Key` header on every
request; the site checks it against `FeatureReviewBot:ApiKey`, injected into the Container App as
an environment variable from a Container App secret (`az containerapp secret set`), never checked
into `appsettings.json`. `ApiKeyEndpointFilter` (`Authorization/ApiKeyEndpointFilter.cs`) rejects
every request outright if that value isn't configured, rather than falling open. `ANTHROPIC_API_KEY`
is also a repo secret. Rotating the key means updating it in both places — the GitHub secret and
the Container App secret — since a mismatch just fails closed (401), not silently.

**Checking it's actually working.** Same habit as the backup workflow: trigger it manually via
`workflow_dispatch` after first setting it up or after any change, and read the "Run the review
bot" step's log — it prints how many proposals it found in each state and what it did with each
one, rather than trusting a green checkmark alone.
