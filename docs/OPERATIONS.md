# Operations

Production infrastructure notes that don't belong in `LOCAL_DEV.md` — that file is about running
this app on your own machine; this one is about the real, permanently-running Azure resources.

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
