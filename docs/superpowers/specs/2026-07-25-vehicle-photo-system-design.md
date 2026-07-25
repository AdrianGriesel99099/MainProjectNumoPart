# Vehicle Photo Documentation System — Application Design

## Context

A car repair workshop documents each vehicle photographically as it moves through the repair process — condition at check-in, quote evidence, work in progress, condition at checkout. Today this lives in manually managed folders (`Checkin`, `Checkout`, `Exstra`, `Quote`, `Progress`), which means finding a specific car's photos requires knowing where to look and browsing by hand.

This spec covers the web application that replaces that: log in, upload photos against a vehicle, and find any car's full photo history by VIN or registration number. Photos frequently serve as condition evidence for disputes and insurance, so durability and traceability matter more than they would for a casual gallery.

The starting point is `MainProjectNumoPart`, an ASP.NET Core Razor Pages app on .NET 8 — currently the default template with no auth, storage, or database.

The Azure footprint this eventually deploys to is specified separately in [2026-07-25-azure-infrastructure-design.md](2026-07-25-azure-infrastructure-design.md).

## Development Approach: Local First

The entire application is built and validated locally before any Azure resource is created. No Azure account, no subscription, no spend during development. Azure provisioning happens only once there is a working app to deploy.

This is achievable because the seam between environments is **configuration, not code** — the same Azure SDK calls run in both places:

| Concern | Local development | Production |
|---|---|---|
| Blob storage | Azurite emulator, well-known dev connection string | Azure Blob Storage, Managed Identity |
| Database | SQLite file in the project directory | SQLite on the mounted Azure Files share |
| Data Protection keys | ASP.NET Core default (local filesystem) | Blob-persisted, Key Vault encrypted |
| HTTPS | ASP.NET dev certificate | Container Apps managed TLS |
| First admin account | Seeded from `appsettings.Development.json` | Seeded from environment config on first run |

Azurite is used rather than a hand-written filesystem fake because SAS token generation is central to how images are served. A filesystem stand-in would require a parallel local-file-serving path, leaving the real production path unexercised until deployment. Azurite supports SAS for real, so the same code runs throughout.

## Domain Model

**Vehicle**
- `Id`, `Vin` (nullable), `Reg` (nullable), `MakeModel` (nullable free text), `BlobFolderName`, `CreatedAtUtc`
- At least one of `Vin` / `Reg` is required — some jobs have only one
- `Vin` and `Reg` each uniquely indexed where non-null
- `BlobFolderName` is assigned once at creation (VIN when present, otherwise Reg) and **never changes thereafter**
- `MakeModel` is optional and never required to upload. It is edited on the vehicle's job page, where a missing VIN or registration discovered later is also filled in — no field beyond the identifiers is asked for at upload time, since the person photographing a car wants to be done quickly

**Photo**
- `Id`, `VehicleId`, `Stage`, `FileName`, `BlobPathOriginal`, `BlobPathThumbnail`, `ContentType`, `SizeBytes`, `UploadedAtUtc`, `DateTakenUtc` (nullable), `SequenceNumber`, `UploaderId`

**Stage** — fixed enum: `Checkin`, `Quote`, `Progress`, `Checkout`, `Extra`. (Corrected from the existing `Exstra` folder spelling.)

Plus standard ASP.NET Core Identity tables for accounts.

### Why BlobFolderName is immutable

A vehicle logged with only a registration number may later have its VIN discovered. If the folder name tracked "VIN when available", that discovery would require physically moving every existing blob — a slow, partially-failable operation that corrupts paths if interrupted. Instead the folder name is frozen at creation and the database maps both identifiers to the same vehicle, so searching either one finds everything while blobs stay put.

## Storage Layout

```
{BlobFolderName}/{Stage}/{filename}
```

Mirrored across two containers at the same path: `originals` (Cold tier) and `thumbnails` (Hot tier).

Filenames encode only the identifiers that actually exist:

| Known identifiers | Filename |
|---|---|
| VIN and Reg | `1HGBH41JXMN109186-VIN-CA481329-Reg-001.jpg` |
| VIN only | `1HGBH41JXMN109186-VIN-001.jpg` |
| Reg only | `CA481329-Reg-001.jpg` |

`SequenceNumber` is allocated per vehicle per stage, so repeated shots at the same stage never collide. Because the app runs at a single replica (a constraint SQLite already imposes), allocating the next number is a plain max+1 query with no concurrency defence needed.

Filenames deliberately repeat identifiers already present in the folder path so that a photo downloaded, emailed, or attached to an insurance claim identifies its vehicle standalone.

`DateTakenUtc` is held in the database rather than encoded in the filename — keeping names short and allowing a mis-entered date to be corrected without renaming blobs.

## Screens

**Login** — invite-only. No public sign-up route exists.

**Search / home** — prominent VIN-or-Reg search, plus recently-worked-on vehicles for quick return.

**Vehicle job page** — the primary screen. Header shows both identifiers, make/model, first-photo date and total count. Stage tabs with counts (an empty stage reads as genuinely empty rather than broken). Photos grouped by stage in strips, each showing its taken-date.

**Upload** — stage dropdown, VIN and Reg fields, optional date-taken, drag-and-drop batch with per-file progress.

**All photos** — flat grid with filters across stage, VIN/Reg, date uploaded and date taken. Answers questions like "what came through in March" where no VIN is at hand — the job page cannot serve this, since it presupposes knowing which vehicle you want.

## Multi-Select and Download

Selection spans stages, with a "select all" affordance per stage. A persistent action bar reports selection count, per-stage breakdown, and total size before the user commits to a download.

Selection is deliberately built as an **action-agnostic primitive**. Download is its first action; a future send/email capability becomes an additional button on the same bar with no change to selection logic.

Downloads are delivered as a zip preserving the stage folder structure inside, mirroring the blob layout. The zip is **streamed** from blob storage through the app rather than assembled in memory — a large selection would otherwise exhaust the deliberately small container.

## Upload Flow

1. User picks a stage, enters VIN and/or Reg, optionally sets a date taken (pre-filled from EXIF where the file carries it, editable, applied across the batch). Reaching Upload from a vehicle's job page pre-fills its identifiers, so adding photos to a car already in the system needs no re-typing.
2. Server validates format (JPEG/PNG only) and size (25MB cap per file).
3. EXIF is read via `MetadataExtractor`; a ~400px thumbnail is generated via `SixLabors.ImageSharp` (chosen over `System.Drawing`, which is not supported cross-platform on Linux containers).
4. An unrecognised VIN/Reg auto-creates the vehicle — there is no separate "add vehicle" step to forget.
5. Original written to the Cold-tier container, thumbnail to Hot, then database rows saved.

VIN input is not format-validated. The workshop's own examples do not follow the 17-character VIN standard, implying internal numbering is in use; rejecting those would block legitimate work.

## Image Serving

Both containers are private. Images are served via short-lived (15 minute), read-only SAS tokens generated on demand, letting the browser fetch bytes directly from blob storage rather than proxying large files through the app. This keeps compute cost and memory pressure down — significant given scale-to-zero sizing. SAS generation is a local HMAC computation with no network round-trip, so generating one per thumbnail in a grid is inexpensive.

## Access Control

- Any authenticated user may upload, browse, and download.
- **Deletion is admin-only.** Check-in photos function as vehicle condition evidence; casual deletion is the wrong default for records that may be needed to settle a dispute.
- `UploaderId` on every photo provides an attribution trail.

## Admin Bootstrapping

Invite-only access creates a chicken-and-egg problem: someone must create the first account. On startup the app seeds an initial admin from configuration if no users exist — from `appsettings.Development.json` locally, and from environment configuration in production. Subsequent accounts are created by an admin through the app.

## Error Handling

- Format and size rejected client-side before any Azure call, and re-validated server-side.
- If a blob write succeeds but the subsequent database save fails, the orphaned blobs are deleted so untracked files cannot silently accumulate and bill.
- Repeated failed logins trigger ASP.NET Core Identity's built-in lockout.
- Transient Blob and SQLite errors rely on the Azure SDK's built-in retry policies rather than bespoke retry code.

## Testing

- **Unit (xUnit):** filter and query logic; filename generation across all three VIN/Reg permutations; sequence allocation; EXIF extraction and thumbnail generation. Blob access sits behind a thin `IPhotoStorage` interface so these run against an in-memory fake with no emulator needed.
- **Integration:** upload and retrieval against Azurite, exercising the real SDK and SAS path.
- **Manual smoke checklist** against real Azure post-deployment: upload → appears on job page → searchable by VIN and by Reg → multi-select download produces a correct zip → login survives a cold start.

## Out of Scope

Deferred deliberately, not overlooked:

- **Sending/emailing images** — the action bar reserves room; explicitly "way later" per the workshop.
- Albums or collections beyond the five fixed stages
- Public or customer-facing share links
- Vehicle service history beyond photographs
- Admin-managed custom stages (the five are hardcoded; adding one is a small code change plus a deploy)
