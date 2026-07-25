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
| Azure Container Apps Environment (Consumption workload profile, scale-to-zero, min replicas 0, max 1) | Hosts the Razor Pages app as a container. Capped at 1 replica so the mounted SQLite file never has two writers at once |
| Azure Files share (Standard LRS, small, mounted to the Container App) | Holds `app.db`, a SQLite database: ASP.NET Core Identity user accounts, vehicle records (VIN, registration, blob folder name) and photo metadata (filename, blob path, stage, uploaded date, taken date/EXIF, sequence number, uploader) — see rationale below |
| Storage Account → Blob Storage, three containers (LRS) | `thumbnails` (Hot tier) for instant grid browsing; `originals` (Cold tier) for full-resolution files — see tiering rationale below. Both are organised as `{vin-or-reg}/{stage}/{filename}` per the application spec. `app-data` (Hot tier) holds the persisted ASP.NET Core Data Protection key ring plus nightly SQLite backups — Hot because both are tiny and get pruned/rotated frequently, which would conflict with Cold's 90-day minimum retention |
| Key Vault (Standard tier) | Holds the RSA key used to encrypt the Data Protection key ring, **and** the Azure Files storage account key (see below) |
| Azure Container Apps Job (Consumption, scheduled/cron trigger, nightly) | Copies `app.db` to the `app-data` Hot blob container as a timestamped backup, pruning anything older than 30 days — mitigates SQLite's lack of automatic backups |
| Log Analytics workspace | Required by Container Apps for logging; free tier covers this volume |
| GitHub Container Registry (ghcr.io) | Stores the built Docker image — free, used instead of Azure Container Registry (~$5/mo) since only one small image is needed |

**Identity & access:** the Container App gets a system-assigned Managed Identity, granted:
- `Storage Blob Data Contributor` on the Storage Account (no connection string needed for blob access, or for the backup job to write to Blob Storage)
- Access to the Key Vault secrets/keys below

**One unavoidable secret:** Container Apps does **not** support Managed Identity for mounting Azure Files (confirmed against Microsoft's current docs and product-team guidance — a raw storage account key is the only supported auth method, and identity-based access isn't on their roadmap). That one key is stored in Key Vault and injected into the Container App as a Key-Vault-backed secret reference (via the app's Managed Identity), rather than hardcoded anywhere. It's the single exception to the "no stored secrets" posture below.

## Why Key Vault, specifically

Scale-to-zero means the container is destroyed and re-created every time it goes idle and someone returns. ASP.NET Core's Data Protection system encrypts auth cookies and antiforgery tokens using a key ring; if that key ring isn't persisted somewhere durable, every cold start invalidates all active logins, forcing constant re-authentication. The fix (a standard, documented pattern for this exact scenario): persist the key ring to Blob Storage (`PersistKeysToAzureBlobStorage`) and encrypt it with a key held in Key Vault (`ProtectKeysWithAzureKeyVault`).

## Why SQLite on Azure Files Instead of a Managed Database

A managed database (Azure SQL Basic) costs ~$7/month flat — which would more than double the total bill below — for a workload that's genuinely small: metadata only (no image bytes), a handful of users, low write volume. SQLite on a small Azure Files share costs closer to $0.10-0.50/month and gives EF Core the same query capability (date-range filters, VIN/registration lookups, stage grouping and uploader attribution all work identically).

Trade-offs accepted: (1) single-writer file locking, mitigated by capping the Container App at 1 replica — not a real loss, since this app was already going to run at low scale; (2) no automatic backups, mitigated by the nightly Container Apps Job that copies the database file to Hot blob storage, pruning snapshots older than 30 days (costs effectively nothing at this file size); (3) the one storage-account-key exception noted above.

## Why Tiered Blob Storage (Hot thumbnails + Cold originals)

Cool and Cold tiers are **not** slower to read than Hot — access latency is the same (milliseconds); only Azure's separate Archive tier has the multi-hour rehydration delay, and that's unsuitable for a browsable site. The real trade-off for Cool/Cold is: lower cost per GB stored, a small per-GB fee when a blob is *read*, and a minimum retention period (30 days for Cool, 90 for Cold) before deleting/re-tiering without an early-deletion charge — all fine for a photo archive that's essentially write-once, read-occasionally.

At 500GB, Cold tier storage (~$0.0045/GB/month) costs roughly a fifth of Hot (~$0.0219/GB/month), and even generous monthly viewing (tens of GB retrieved) only adds a fraction of a dollar in retrieval fees. Thumbnails stay in Hot tier since they're small in aggregate (tens of GB even for a very large photo count) and are read constantly during browsing — Hot avoids per-read retrieval fees for that access pattern.

**Bulk downloads don't overturn this.** The application supports multi-select zip downloads of originals, which draw Cold retrieval fees ($0.03/GB vs Cool's $0.01/GB). But Cold saves ~$3.70/month on storage at 500GB versus Cool, so downloads would need to exceed ~185GB/month — on the order of 1,500 full-job downloads — before Cool became the cheaper choice. That is far beyond a workshop's realistic usage, so Cold stands.

## Cost Estimate (South Africa North, 500GB of originals)

| Resource | USD/month | ZAR/month (≈R16.82/USD, 2026-07-24) |
|---|---|---|
| Container Apps (scale-to-zero) | ~$0-3 | ~R0-50 |
| Azure Files (SQLite host, <1GB) | ~$0.10-0.50 | ~R2-8 |
| Blob Storage — originals, Cold tier, 500GB (storage + retrieval) | ~$2.25-3 | ~R38-50 |
| Blob Storage — thumbnails + Data Protection keys + DB backups, Hot tier, ~20GB | ~$0.50 | ~R8 |
| Key Vault | <$0.10 | <R2 |
| Container Apps Job (nightly backup, seconds of runtime) | ~$0 (within free grant) | ~R0 |
| Log Analytics + GitHub Container Registry | $0 | R0 |
| **Total** | **~$3-7/month** | **~R50-120/month** |

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
