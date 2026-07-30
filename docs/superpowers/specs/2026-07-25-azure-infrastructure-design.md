# Azure Infrastructure Design — Vehicle Photo Documentation System

## Context

This is the infrastructure sub-project for a car repair workshop's vehicle photo documentation system (login-protected upload, search by VIN/registration, and browsing of repair-process photos). The application itself (`MainProjectNumoPart`) is an ASP.NET Core Razor Pages app on .NET 8, currently just the default template with no auth, storage, or database wired up. This spec covers the Azure footprint the app will run on; the application design is specified in [2026-07-25-vehicle-photo-system-design.md](2026-07-25-vehicle-photo-system-design.md).

## Sequencing: Local First

The application is built and validated entirely locally — Azurite for blob storage, a local SQLite file, default Data Protection — before any Azure resource is created. **Nothing in this spec is provisioned, and no spend begins, until there is a working application to deploy.** The seam between environments is configuration rather than code, so the same SDK calls run in both. See the application spec for the environment configuration table.

## Goals

- Minimum viable, cheapest reliable Azure footprint for a workshop of roughly 10-20 staff, not the general public.
- Single production environment — no separate dev/staging Azure resources.
- Budget priority: as cheap as possible without sacrificing basic reliability or security.
- Storage volume: up to ~500GB of original photos, growing over time.
- Photos serve as vehicle condition evidence for disputes and insurance claims, so durability and a reliable backup path carry more weight than they would for casual photo storage.

## Region & Subscription

- New Azure subscription, Pay-As-You-Go billing. Account creation and payment details are handled by the user directly (not automatable).
- Region: **South Africa North** (ZA North).
- Single environment: production only.

## Resources

| Resource | Purpose |
|---|---|
| Resource Group (`rg-workshop-photos-prod`) | Container for all resources below |
| Azure Container Apps Environment (Consumption workload profile, scale-to-zero, min replicas 0, max 1) | Hosts the Razor Pages app as a container |
| Azure SQL Database (Basic tier, `sql-workshop-photos-prod` / `sqldb-workshop-photos-prod`) | Holds ASP.NET Core Identity user accounts, vehicle records (VIN, registration, blob folder name) and photo metadata (filename, blob path, stage, uploaded date, taken date/EXIF, sequence number, uploader). Azure AD-only authentication — no SQL password exists anywhere for this server. See "Why Azure SQL, Not SQLite on Files" below for how this superseded the original plan |
| Storage Account → Blob Storage, three containers (LRS) | `thumbnails` (Hot tier) for instant grid browsing; `originals` (Cold tier) for full-resolution files — see tiering rationale below. Both are organised as `{vin-or-reg}/{stage}/{filename}` per the application spec. `app-data` (Hot tier) holds the persisted ASP.NET Core Data Protection key ring — Hot because it's tiny and gets rewritten occasionally, which would conflict with Cold's 90-day minimum retention |
| Key Vault (Standard tier) | Holds the RSA key used to encrypt the Data Protection key ring |
| Log Analytics workspace | Required by Container Apps for logging; free tier covers this volume |
| GitHub Container Registry (ghcr.io) | Stores the built Docker image — free, used instead of Azure Container Registry (~$5/mo) since only one small image is needed |

**Identity & access:** the Container App gets a system-assigned Managed Identity, granted:
- `Storage Blob Data Contributor` on the Storage Account (no connection string needed for blob access)
- A SQL database user (`db_datareader` + `db_datawriter`), created via `CREATE USER [ca-workshop-photos-prod] FROM EXTERNAL PROVIDER` against the AAD-only-auth server — the connection string uses `Authentication=Active Directory Managed Identity`, so `Microsoft.Data.SqlClient` acquires and refreshes the token itself
- `Key Vault Secrets User` and `Key Vault Crypto User` on the Key Vault

**No stored secrets at all**, for either the database or blob storage — both go through Managed Identity end to end. The one exception is the GHCR registry pull credential (the GitHub repo is private), stored as a Container App secret rather than a Key Vault reference for simplicity; low stakes since it only grants pulling this one image.

## Why Key Vault, specifically

Scale-to-zero means the container is destroyed and re-created every time it goes idle and someone returns. ASP.NET Core's Data Protection system encrypts auth cookies and antiforgery tokens using a key ring; if that key ring isn't persisted somewhere durable, every cold start invalidates all active logins, forcing constant re-authentication. The fix (a standard, documented pattern for this exact scenario): persist the key ring to Blob Storage (`PersistKeysToAzureBlobStorage`) and encrypt it with a key held in Key Vault (`ProtectKeysWithAzureKeyVault`).

## Why Azure SQL, Not SQLite on Files (revised after deployment)

**This plan originally specified SQLite on a mounted Azure Files share**, reasoning that the ~$7/month cost of a managed database wasn't justified for a workload this small (metadata only, low write volume). That reasoning was sound on paper but wrong in practice: **deployed and tested against the real Container Apps + Azure Files combination, every database write failed with `SQLite Error 5: 'database is locked'`** — confirmed across three independent fresh container revisions, and ruled out as a pooling or caching artifact (tested `Pooling=false` and `Cache=Shared` in the connection string; both failed identically). This is a known, documented category of incompatibility: SQLite's file-locking model doesn't work reliably over SMB network shares, Azure Files included, regardless of how light the actual write concurrency is.

The app now uses **Azure SQL Database, Basic tier (~$7/month)** instead. This is a net simplification, not just a cost increase: it removed the Azure Files share, the Azure Files storage account, the "one unavoidable secret" (the Files account key) that was the single exception to this design's no-stored-secrets posture, and the nightly Container Apps Job that existed solely to work around SQLite's lack of automatic backups (Azure SQL Basic includes built-in point-in-time restore). Local development is unaffected — it still uses plain SQLite with zero setup, since Azure Files was never part of the local-dev path to begin with.

If a future revision of this plan wants to reconsider SQLite for cost reasons, the fix is not "try harder to make SQLite work on Files" — it would need genuinely durable single-instance local disk (which Container Apps' ephemeral storage does not provide across restarts) or a different network filesystem with real POSIX locking support.

## Why Tiered Blob Storage (Hot thumbnails + Cold originals)

Cool and Cold tiers are **not** slower to read than Hot — access latency is the same (milliseconds); only Azure's separate Archive tier has the multi-hour rehydration delay, and that's unsuitable for a browsable site. The real trade-off for Cool/Cold is: lower cost per GB stored, a small per-GB fee when a blob is *read*, and a minimum retention period (30 days for Cool, 90 for Cold) before deleting/re-tiering without an early-deletion charge — all fine for a photo archive that's essentially write-once, read-occasionally.

At 500GB, Cold tier storage (~$0.0045/GB/month) costs roughly a fifth of Hot (~$0.0219/GB/month), and even generous monthly viewing (tens of GB retrieved) only adds a fraction of a dollar in retrieval fees. Thumbnails stay in Hot tier since they're small in aggregate (tens of GB even for a very large photo count) and are read constantly during browsing — Hot avoids per-read retrieval fees for that access pattern.

**Bulk downloads don't overturn this.** The application supports multi-select zip downloads of originals, which draw Cold retrieval fees ($0.03/GB vs Cool's $0.01/GB). But Cold saves ~$3.70/month on storage at 500GB versus Cool, so downloads would need to exceed ~185GB/month — on the order of 1,500 full-job downloads — before Cool became the cheaper choice. That is far beyond a workshop's realistic usage, so Cold stands.

## Cost Estimate (South Africa North, 500GB of originals)

| Resource | USD/month | ZAR/month (≈R16.82/USD, 2026-07-24) |
|---|---|---|
| Container Apps (scale-to-zero) | ~$0-3 | ~R0-50 |
| Azure SQL Database (Basic tier) | ~$7 (flat, DTU-based) | ~R118 |
| Blob Storage — originals, Cold tier, 500GB (storage + retrieval) | ~$2.25-3 | ~R38-50 |
| Blob Storage — thumbnails + Data Protection keys, Hot tier, ~20GB | ~$0.50 | ~R8 |
| Key Vault | <$0.10 | <R2 |
| Log Analytics + GitHub Container Registry | $0 | R0 |
| **Total** | **~$10-14/month** | **~R168-235/month** |

(Superseded the original ~$3-7/month estimate, which assumed SQLite on Azure Files — see "Why Azure SQL, Not SQLite on Files" above for why that didn't hold up in practice.)

Optional, separate from Azure: a custom domain (~$10-15/year ≈ R170-250/year). The default `*.azurecontainerapps.io` hostname is free with automatic managed HTTPS, so a custom domain is a nice-to-have, not required at launch.

Exchange rate fluctuates and Azure's actual invoice currency/rate depends on how the billing account is configured — treat the ZAR figures as an estimate; the underlying USD meter prices are the source of truth.

## Security Posture

- HTTPS enforced by default (Container Apps provides free managed TLS on the default domain).
- Almost no secrets in code or app settings — Managed Identity for Blob Storage access; Key Vault holds the Data Protection encryption key and the one unavoidable Azure Files account key (see above), neither hardcoded anywhere.
- ASP.NET Core Identity handles password hashing (PBKDF2) out of the box.

## Out of Scope (covered by the application spec)

- Login/auth pages, upload, search, job view, filtering, multi-select and download UX
- Database schema, blob path and filename conventions
- Local development setup (Azurite, local SQLite)

Also out of scope here: CI/CD workflow implementation details (GitHub Actions build/push/deploy) — the registry choice above constrains it, but the workflow itself is an implementation detail.
