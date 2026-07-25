# Azure Infrastructure Design — Private Photo Gallery

## Context

This is the infrastructure sub-project for a private photo gallery website (login-protected image upload/browse/search). The application itself (`MainProjectNumoPart`) is an ASP.NET Core Razor Pages app on .NET 8, currently just the default template with no auth, storage, or database wired up. This spec covers the Azure footprint the app will run on; the application design (auth, upload, browse/search/filter UI) is a separate spec.

## Goals

- Minimum viable, cheapest reliable Azure footprint for a private gallery used by roughly 10-20 known people (small team/family), not the general public.
- Single production environment — no separate dev/staging Azure resources.
- Budget priority: as cheap as possible without sacrificing basic reliability or security.
- Storage volume: up to ~500GB of original photos, growing over time. A few seconds' extra latency for rarely-viewed originals is acceptable (it enables much cheaper storage tiers — see below).

## Region & Subscription

- New Azure subscription, Pay-As-You-Go billing. Account creation and payment details are handled by the user directly (not automatable).
- Region: **South Africa North** (ZA North).
- Single environment: production only.

## Resources

| Resource | Purpose |
|---|---|
| Resource Group (`rg-<project>-prod`) | Container for all resources below |
| Azure Container Apps Environment (Consumption workload profile, scale-to-zero, min replicas 0) | Hosts the Razor Pages app as a container |
| Azure SQL Database (Basic tier, 2GB) | ASP.NET Core Identity user accounts + image metadata (filename, blob path, uploaded date, taken date/EXIF, tags, uploader) |
| Storage Account → Blob Storage, two containers (LRS) | `thumbnails` (Hot tier) for instant grid/search browsing; `originals` (Cold tier) for full-resolution files — see tiering rationale below. A third container holds the persisted ASP.NET Core Data Protection key ring |
| Key Vault (Standard tier) | Holds the RSA key used to encrypt the Data Protection key ring (see below) — not general-purpose secret storage |
| Log Analytics workspace | Required by Container Apps for logging; free tier covers this volume |
| GitHub Container Registry (ghcr.io) | Stores the built Docker image — free, used instead of Azure Container Registry (~$5/mo) since only one small image is needed |

**Identity & access:** the Container App gets a system-assigned Managed Identity, granted:
- `Storage Blob Data Contributor` on the Storage Account (no connection string needed for blob access)
- Azure AD-based access to the SQL Database (no password/connection secret stored anywhere)
- Access to the Key Vault key used for Data Protection

## Why Key Vault, specifically

Scale-to-zero means the container is destroyed and re-created every time it goes idle and someone returns. ASP.NET Core's Data Protection system encrypts auth cookies and antiforgery tokens using a key ring; if that key ring isn't persisted somewhere durable, every cold start invalidates all active logins, forcing constant re-authentication. The fix (a standard, documented pattern for this exact scenario): persist the key ring to Blob Storage (`PersistKeysToAzureBlobStorage`) and encrypt it with a key held in Key Vault (`ProtectKeysWithAzureKeyVault`).

## Why Tiered Blob Storage (Hot thumbnails + Cold originals)

Cool and Cold tiers are **not** slower to read than Hot — access latency is the same (milliseconds); only Azure's separate Archive tier has the multi-hour rehydration delay, and that's unsuitable for a browsable site. The real trade-off for Cool/Cold is: lower cost per GB stored, a small per-GB fee when a blob is *read*, and a minimum retention period (30 days for Cool, 90 for Cold) before deleting/re-tiering without an early-deletion charge — all fine for a photo archive that's essentially write-once, read-occasionally.

At 500GB, Cold tier storage (~$0.0045/GB/month) costs roughly a fifth of Hot (~$0.0219/GB/month), and even generous monthly viewing (tens of GB retrieved) only adds a fraction of a dollar in retrieval fees. Thumbnails stay in Hot tier since they're small in aggregate (tens of GB even for a very large photo count) and are read constantly during browsing — Hot avoids per-read retrieval fees for that access pattern.

## Cost Estimate (South Africa North, 500GB of originals)

| Resource | USD/month | ZAR/month (≈R16.82/USD, 2026-07-24) |
|---|---|---|
| Container Apps (scale-to-zero) | ~$0-3 | ~R0-50 |
| Azure SQL Database (Basic) | ~$7 flat | ~R118 |
| Blob Storage — originals, Cold tier, 500GB (storage + retrieval) | ~$2.25-3 | ~R38-50 |
| Blob Storage — thumbnails, Hot tier, ~20GB | ~$0.50 | ~R8 |
| Key Vault | <$0.10 | <R2 |
| Log Analytics + GitHub Container Registry | $0 | R0 |
| **Total** | **~$13-16/month** | **~R220-270/month** |

Optional, separate from Azure: a custom domain (~$10-15/year ≈ R170-250/year). The default `*.azurecontainerapps.io` hostname is free with automatic managed HTTPS, so a custom domain is a nice-to-have, not required at launch.

Exchange rate fluctuates and Azure's actual invoice currency/rate depends on how the billing account is configured — treat the ZAR figures as an estimate; the underlying USD meter prices are the source of truth.

## Security Posture

- HTTPS enforced by default (Container Apps provides free managed TLS on the default domain).
- No secrets in code or app settings — Managed Identity for Storage and SQL access, Key Vault only for the Data Protection encryption key.
- ASP.NET Core Identity handles password hashing (PBKDF2) out of the box.

## Out of Scope (separate spec)

- Login/auth pages, image upload/browse/search/filter UI and UX
- Exact database schema beyond "user accounts + image metadata"
- CI/CD workflow implementation details (GitHub Actions build/push/deploy) — the registry choice above constrains it, but the workflow itself is an implementation detail
