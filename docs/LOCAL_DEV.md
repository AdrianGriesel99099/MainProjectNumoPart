# Local Development Setup

This app runs entirely locally with no Azure account or spend. Everything in
production (Blob Storage, Key Vault, Container Apps) is swapped in later via
configuration only — see `docs/superpowers/specs/2026-07-25-azure-infrastructure-design.md`.

## Prerequisites

- .NET 8 SDK
- `dotnet-ef` (EF Core CLI tool), needed for the migration command in step 3 below:
  `dotnet tool install --global dotnet-ef`.
- Azurite (Azure Storage emulator). Easiest: Visual Studio 2022 → **Tools → Azurite → Start Azurite**.
  Alternative: `npx azurite --skipApiVersionCheck` (no global install needed), or
  `docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite`.
  **The `--skipApiVersionCheck` flag matters**: with Azurite 3.36.0 and `Azure.Storage.Blobs` 12.29.1
  (the versions this project uses), blob requests fail with a 400 "API version not supported"
  error without it.

## First-time setup

1. Start Azurite (see above) and leave it running.
2. Restore and build:
   ```bash
   dotnet restore
   dotnet build
   ```
3. Apply migrations to create the local SQLite database:
   ```bash
   dotnet ef database update --project MainProjectNumoPart.csproj
   ```
   This creates `app.db` in the project root (gitignored — it's your local data, not shared).
4. Confirm `appsettings.Development.json` has an `InitialAdmin` section with an email/password —
   this account is auto-created the first time the app runs against an empty database.
5. Run the app:
   ```bash
   dotnet run --project MainProjectNumoPart.csproj
   ```
6. Sign in at `/Account/Login` with the `InitialAdmin` credentials.

## Running tests

```bash
dotnet test MainProjectNumoPart.Tests
```

Most tests need nothing beyond the .NET SDK. `BlobPhotoStorageTests` needs Azurite running —
they'll fail with a connection error (not a false pass) if it isn't.

## Roles

Every account has exactly one role. All three are created automatically at startup.

| Role | Can do |
|---|---|
| `Viewer` | Search, browse vehicles and photos, download zips |
| `Staff` | Everything a Viewer can, plus upload photos |
| `Admin` | Everything Staff can, plus delete photos, delete vehicles, and manage accounts |

The account seeded from `InitialAdmin:Email` / `InitialAdmin:Password` is an `Admin`. Every other
account is created by an admin at **`/Admin/Users`**, which is also where you change someone's role
later. There is no self-service sign-up.

Two guards you'll hit if you go looking for them: you cannot remove your own Admin role (ask
another admin), and the last remaining Admin cannot be demoted — otherwise nobody could ever
manage accounts again.

**A role change takes effect within about a minute**, not instantly. Role claims live in the
signed-in user's auth cookie; `SecurityStampValidator` re-checks it on the interval set in
`Program.cs` (1 minute) and signs them out when it changes. If you're testing a demotion, wait a
minute before concluding it didn't work.

Accounts created before roles existed have no role at all. They show as **None** in the users
table and can only browse — assign them a role there.

## Resetting local data

Delete `app.db` (and `app.db-shm`/`app.db-wal` if present), then re-run
`dotnet ef database update`. To clear uploaded photos too, stop Azurite, delete its
data (the `__azurite_db*__.json` files and `__blobstorage__` folder in whatever directory
you started it from), and restart it.

## Manual smoke checklist

After any significant change, confirm end-to-end by hand:

- [ ] Log in as the seeded admin.
- [ ] Upload 2-3 photos (mix of JPEG and PNG) against a new VIN, no Reg.
- [ ] Search by that VIN — lands on the vehicle's job page, thumbnails render.
- [ ] Upload another photo against the same VIN plus a Reg this time — confirm it lands
      on the *same* vehicle (find-or-create matched by VIN) and the Reg now shows on the page.
- [ ] Search by that Reg — same vehicle is found.
- [ ] Visit `/Photos`, filter by stage and by VIN — results narrow correctly.
- [ ] Select 2+ photos on the job page and download a zip — confirm the stage-folder
      structure inside it.
- [ ] As a non-admin account, confirm the × delete button is not visible, and a direct
      `DELETE /api/photos/{id}` request is rejected.
- [ ] As admin, delete a photo — confirms it disappears from both the page and Azurite storage.

### Upload context

- [ ] From a vehicle page, click **+ Upload** — the banner names that vehicle and VIN/Reg are
      prefilled.
- [ ] Submit with no files selected — the error re-renders with the banner *still present* and the
      fields *still prefilled*.
- [ ] Use the nav-bar **Upload** link instead — blank form, no banner (this path is unchanged and
      must stay that way: photographing a car that isn't in the system yet is the primary workflow).
- [ ] Visit `/Upload?vehicleId=999999` — blank form, no banner, no error.

### Roles

- [ ] As a **Viewer**: no Upload or Users links in the nav; a search miss shows no upload link;
      `/Upload` and `/Admin/Users` both land on the friendly **Access denied** page (not a 404);
      `/Photos`, filtering and zip download all still work.
- [ ] View source on a vehicle page as a Viewer — confirm *no* delete markup or delete JavaScript
      is emitted at all, not merely hidden.
- [ ] As **Staff**: upload works; no × buttons; no danger zone; `/Admin/Users` denied.
- [ ] Create one account of each role — the form redirects (F5 doesn't resubmit) and the temporary
      password renders as dots, not plain text.
- [ ] Demote a signed-in Staff user to Viewer in another browser — within ~1 minute they lose
      Upload.
- [ ] Try to demote yourself, and try to demote the only Admin — both are refused with an
      explanatory message.

### Vehicle deletion

- [ ] Wrong confirmation text → inline error, and every blob is still in Azurite.
- [ ] Empty confirmation → same refusal.
- [ ] Lowercase and/or spaced identifier (`ab12 cde` for `AB12CDE`) → **accepted**.
- [ ] Correct text → lands on Home, gone from "Recently added", the details page 404s, and the
      vehicle's blobs are gone from **both** the originals and thumbnails containers.
- [ ] Another vehicle's photos and blobs are untouched.
- [ ] As Staff, `curl -X DELETE /api/vehicles/{id}` with a *correct* confirmation → still refused.
