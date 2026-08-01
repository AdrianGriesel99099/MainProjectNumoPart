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

### Editing a vehicle

- [ ] From a vehicle page, **Edit details** — VIN, registration and make/model are prefilled
      (make/model has no other way to be set).
- [ ] Change the VIN to one another vehicle already uses → refused with a clear message, and both
      records are left alone.
- [ ] Clear both the VIN and registration → refused; a vehicle must keep at least one.
- [ ] Enter a lowercase, spaced registration → saved normalised (`zz11 aaa` becomes `ZZ11AAA`).
- [ ] **Change the VIN on a vehicle that already has photos, then reload the page — the thumbnails
      must still render.** Existing photos keep their original blob paths by design; the folder
      name is an immutable storage key, so it stops matching the VIN after a correction and that
      is expected.
- [ ] As a Viewer, `/Vehicles/Edit/{id}` → Access denied, and no Edit button is shown.

### Vehicle deletion

- [ ] Wrong confirmation text → inline error, and every blob is still in Azurite.
- [ ] Empty confirmation → same refusal.
- [ ] Lowercase and/or spaced identifier (`ab12 cde` for `AB12CDE`) → **accepted**.
- [ ] Correct text → lands on Home, gone from "Recently added", the details page 404s, and the
      vehicle's blobs are gone from **both** the originals and thumbnails containers.
- [ ] Another vehicle's photos and blobs are untouched.
- [ ] As Staff, `curl -X DELETE /api/vehicles/{id}` with a *correct* confirmation → still refused.

### Tagging photos by part

Photos carry an optional `Part` (front bumper, left door, etc.) independent of `Stage` — a photo
has both. Tagging is designed to happen **after** upload in bulk, since the real workflow is
shooting a burst of photos and sorting them afterwards.

- [ ] Upload a batch with no part set — all land untagged; `/Photos?PartFilter=untagged` finds
      exactly them. This is the worklist for photos still needing attention.
- [ ] On a vehicle page, select several photos and use **Set part…** in the selection bar — a
      badge appears on each; choosing **— clear part —** removes it.
- [ ] Click **Pick on car** in the selection bar — a rotatable 3D car appears. Drag to orbit,
      click a panel; the compact dropdown next to it updates to match. If WebGL is unavailable
      (or the model fails to load within 8s), a flat diagram with the same clickable regions
      appears instead — the picker never becomes unusable, only less impressive.
- [ ] At the top of a vehicle page, the **coverage map** shades every part that has at least one
      photo and leaves the rest blank. Click a shaded part to filter the photo grid to it; click
      "Show all" to clear the filter. This is deliberately a flat diagram, not the 3D picker — a
      panel highlighted on the far side of a rotating model is invisible, which defeats a map
      whose entire point is showing everything at once.
- [ ] As Viewer, `POST /api/photos/part` with a valid body → refused (302 to AccessDenied).
      As **Staff** (not just Admin) with the same body → succeeds. Checking the success case
      matters here specifically: `RequireRole` takes `params string[]`, so a comma-joined role
      string would deny everyone, Staff included, and only a failure-only test would miss that.
- [ ] Re-tag a photo that already has a part — the old badge is replaced, not duplicated.

**Regenerating the 3D model.** `wwwroot/models/car.glb` and `car-parts.json` are generated, not
hand-authored, and both are committed so CI never needs Blender installed. To regenerate after
changing `Models/Part.cs` or swapping the source mesh:

```bash
blender --background --python tools/car-model/build_car.py
```

This reads `tools/car-model/source/car_mesh.glb` (a real sedan mesh, not authored by this repo —
see the licensing note at the top of `build_car.py` before redistributing it further than
internal use), recolours it, and generates 49 hitboxes — one per `Part` enum member, most visible
and clickable, six hidden (`hide_render=True`) for parts with no exterior shape — positioned as
fractions of the mesh's own bounding box. `CarModelManifestTests` will fail the build if the two
ever drift (an enum member renamed without regenerating the model, or vice versa). If no source
mesh is available, `tools/car-model/build_car_primitive.py` builds a car from primitives instead
and is a straight drop-in replacement — same output paths, same manifest format.

### Deleting a user

- [ ] On `/Admin/Users`, your own row has no Delete button at all — it isn't just disabled, it
      isn't rendered.
- [ ] Delete another admin while at least one other admin remains → succeeds; the deleted account
      can no longer log in.
- [ ] Try to delete the **last** remaining admin (via a direct `POST /Admin/Users?handler=Delete`,
      since the button is UI-hidden for your own row but this guard is independent of that) →
      refused with "This is the last Admin account."
- [ ] Delete a user who has uploaded photos → succeeds, and their old photos still render fine.
      `Photos.UploaderId` has no FK — this is deliberate, matching the same tradeoff
      `VehicleDeletionService` already makes for vehicle deletes.

### Job card and photo comments

Two independent free-text logs: a job card per vehicle (`VehicleUpdate`), and comments per photo
(`PhotoComment`). Both are Staff/Admin to write, everyone to read, Admin-only to delete.

- [ ] Post a job-card update as Staff — appears immediately with your email and timestamp; as
      Viewer, the add-update form simply isn't shown, and posting directly via
      `POST /api/vehicles/{id}/updates` → refused. **Confirm Staff specifically succeeds**, not
      just that Viewer is refused — `RequireRole(Roles.Staff, Roles.Admin)` must stay two
      arguments; the comma-joined `Roles.StaffOrAdmin` constant would silently deny Staff too, and
      a denial-only check wouldn't catch that.
- [ ] Delete a job-card entry as Admin → gone; as Staff → refused.
- [ ] Open a photo via the small view icon (⤢) on a vehicle page's photo tile — the rest of the
      tile still toggles multi-select exactly as before; the icon itself does not.
- [ ] On `/Photos`, click a tile — lands on that photo's page (`/Photos/View/{id}`), not the
      vehicle page.
- [ ] On a photo's page, both dates are shown — "Uploaded" always has a value; "Taken" shows
      "Not recorded" for a photo with no EXIF/manual date rather than a blank or an error.
- [ ] Post and delete a photo comment, same role rules as the job card above.
- [ ] Delete the user who wrote a comment or update, then reload the page it's on — the entry
      still shows their original email. This is `AuthorEmail`, captured once at write time
      specifically so a later account deletion can't make old comments unattributed.

### Upload feedback

- [ ] Click Upload on a real multi-file batch — the button darkens immediately and swaps to a
      spinner with "Uploading…" before the page navigates away.
- [ ] Click it a second time while it's still processing — nothing happens (the button is
      disabled, so the batch can't be submitted twice).

### The 16 panel-beater parts

Front-end terminology added on top of the original 33: front/rear spoiler, main/centre grill,
left/right spotlamp and spotlamp grill, left/right front bumper grill (all with real diagram/3D
regions), plus left/right front fenderliner and left/right front/rear bumper slide (button-only —
none of the six are visible from outside the car, so they live alongside Interior/Engine bay
rather than getting an invented hotspot).

- [ ] On the front view of the 2D diagram, each of the ten new regions is individually clickable
      without accidentally selecting a neighbour — Spotlamp nests inside Spotlamp grill inside
      Bumper grill, and Centre grill inside Main grill, so click near the centre of the smallest
      shape first.
- [ ] The six button-only parts appear in the extras row and in the plain `<select>` fallback; a
      photo can be tagged to any of the 16 like any other part.
- [ ] In the 3D picker, rotating past the nested front-corner parts (Spotlamp inside its grill
      inside the bumper grill) — the innermost one is reachable by click, not permanently occluded
      by its parent's larger hitbox. (This nesting is exactly why 3D hitbox overlap resolves by
      camera distance, not click order — if the innermost part were ever unreachable after a
      future `build_car.py` change, this is the symptom to watch for.)

### 3D panel lines

Every hitbox in the 3D model now renders a thin, low-opacity outline (`THREE.EdgesGeometry` +
`LineSegments`, added client-side in `car3d.js` — no Blender regeneration needed for this part).
Hitboxes are deliberately oversized past the visible body hull and have no shared-edge data, so
these are an approximation, not literal panel-gap geometry.

- [ ] Open the 3D picker and rotate the car — every visible panel has a faint separating line from
      its neighbours, not just the currently-selected one, and it's genuinely legible rather than
      a noisy tangle of disconnected boxes.

### Damage marking

Clicking a part on the always-rendered **Mark damage** diagram (works even before any photo
exists) opens a popup: the vehicle's real tagged photo if one exists for that part, otherwise a
zoomed diagram shape (via `getBBox()` on a clone of the part's own SVG region — no new artwork).
Either way, clicking anywhere that isn't an existing pin starts a new one; existing pins render as
small dots with a tooltip, never burned into the image itself. A second, permanent "Damage marks"
list on the vehicle page is the browsable record — part, thumbnail-or-"(diagram)", note, author,
timestamp.

- [ ] Click a part with **no** tagged photo — popup opens on the diagram tab, zoomed to just that
      shape.
- [ ] Click inside it (not on an existing dot) — a pin drops where you clicked and a note field
      appears; Save → the mark appears both in the popup and in the "Damage marks" list.
- [ ] Tag a photo to that same part, reopen the popup for it — now defaults to the **photo** tab;
      the earlier diagram-anchored mark is untouched (still listed, still diagram-anchored).
- [ ] Delete the photo a mark was anchored to — that mark disappears too (cascade); any
      diagram-anchored marks for the same part are unaffected.
- [ ] As Staff, add a mark → succeeds; as Viewer, `POST /api/vehicles/{id}/damage-marks` → refused.
      **Confirm Staff specifically succeeds**, not just that Viewer is refused — same
      `RequireRole(Roles.Staff, Roles.Admin)` two-argument trap as the job card and photo comments.
- [ ] Delete a mark as Admin → gone; as Staff → refused (delete is Admin-only, unlike add).
- [ ] Click one of the six button-only parts (e.g. a fenderliner) with nothing tagged and no
      diagram shape — popup still opens, shows "No photo or diagram shape for this part yet — you
      can still record a note," and saving still works (fixed centre position rather than a real
      pin, since there's nothing to click on).
