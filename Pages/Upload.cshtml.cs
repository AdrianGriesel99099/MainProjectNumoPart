// Pages/Upload.cshtml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs.Models;
using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages
{
    [Authorize(Roles = Roles.StaffOrAdmin)]
    public class UploadModel : PageModel
    {
        private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png"
        };
        private const long MaxFileSizeBytes = 25 * 1024 * 1024;

        private readonly AppDbContext _db;
        private readonly VehicleLookupService _vehicles;
        private readonly PhotoSequenceAllocator _sequencer;
        private readonly IPhotoStorage _storage;
        private readonly UserManager<IdentityUser> _userManager;

        public UploadModel(AppDbContext db, VehicleLookupService vehicles, PhotoSequenceAllocator sequencer,
            IPhotoStorage storage, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _vehicles = vehicles;
            _sequencer = sequencer;
            _storage = storage;
            _userManager = userManager;
        }

        [BindProperty] public string? Vin { get; set; }
        [BindProperty] public string? Reg { get; set; }
        [BindProperty] public Stage Stage { get; set; }

        // Optional, and applies to the whole batch exactly like Stage does. Most uploads leave
        // it unset — the realistic flow is shooting a burst and tagging afterwards — but when
        // someone is deliberately photographing one panel it saves a second pass.
        [BindProperty] public Part? Part { get; set; }
        [BindProperty] public DateTime? DateTaken { get; set; }
        [BindProperty] public List<IFormFile> Files { get; set; } = new();

        // SupportsGet binds this from ?vehicleId=N when arriving from a vehicle page AND from the
        // hidden form field on POST — which is what keeps the context banner alive when validation
        // fails and the page re-renders.
        [BindProperty(SupportsGet = true)] public int? VehicleId { get; set; }

        // Display only. Never used to resolve which vehicle the photos are filed under — see the
        // note at the FindOrCreateAsync call below.
        public Vehicle? ContextVehicle { get; set; }

        public string? ErrorMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadVehicleContextAsync();

            if (ContextVehicle is null)
            {
                // Unknown or absent id: fall back to the blank form exactly as before. Clearing
                // VehicleId stops a stale value riding along in the hidden field. The nav-bar
                // Upload link deliberately passes no vehicleId, and that path must not change.
                VehicleId = null;
                return;
            }

            // Prefill on GET only. On POST these come from the form, because the user is allowed
            // to correct them.
            Vin = ContextVehicle.Vin;
            Reg = ContextVehicle.Reg;
        }

        private async Task LoadVehicleContextAsync()
        {
            if (VehicleId is null) return;

            ContextVehicle = await _db.Vehicles
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == VehicleId.Value);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Loaded once at the top so every `return Page()` below re-renders with the context
            // banner intact. Doing it per-branch would mean five call sites and a near-certainty
            // that a future sixth one forgets.
            await LoadVehicleContextAsync();

            if (string.IsNullOrWhiteSpace(Vin) && string.IsNullOrWhiteSpace(Reg))
            {
                ErrorMessage = "Enter a VIN, a registration number, or both.";
                return Page();
            }

            if (Files.Count == 0)
            {
                ErrorMessage = "Choose at least one photo.";
                return Page();
            }

            foreach (var file in Files)
            {
                if (!AllowedContentTypes.Contains(file.ContentType))
                {
                    ErrorMessage = $"{file.FileName}: only JPEG and PNG are supported.";
                    return Page();
                }
                if (file.Length > MaxFileSizeBytes)
                {
                    ErrorMessage = $"{file.FileName}: exceeds the 25MB limit.";
                    return Page();
                }
            }

            var userId = _userManager.GetUserId(User)!;
            Vehicle vehicle;
            try
            {
                // Resolution is by the SUBMITTED TEXT, never by VehicleId — VehicleId is display
                // context only. That's intentional: arriving from a vehicle page and correcting a
                // typo in the VIN must re-file the photos under the corrected vehicle, which is
                // exactly what leaving the fields editable is for.
                vehicle = await _vehicles.FindOrCreateAsync(Vin, Reg);
                // Commits, assigning vehicle.Id before it's used in blob paths below. Two
                // concurrent uploads for the same brand-new VIN/Reg can both reach this point
                // having seen "nothing exists yet" — SaveWithRetryAsync is what turns the
                // loser's unique-index violation into a transparent re-resolve instead of an
                // unhandled 500.
                vehicle = await _vehicles.SaveWithRetryAsync(vehicle, Vin, Reg);
            }
            catch (VehicleIdentifierConflictException ex)
            {
                // VIN and Reg point at two different existing vehicles — almost always a typo.
                // (SaveWithRetryAsync's own re-resolve can theoretically throw this too, if a
                // second concurrent request's create is what the retry collides with — same
                // friendly treatment either way.) No blobs have been touched yet, so nothing to
                // clean up here.
                ErrorMessage = ex.Message;
                return Page();
            }

            // Retries up to 3 times total. Guards against a race the first version of this method
            // didn't: two concurrent uploads to the SAME vehicle+stage can both read the same
            // "next" sequence number before either commits — "single instance" (Global Constraints)
            // rules out multiple *replicas*, not multiple *concurrent requests* within the one
            // process, which ASP.NET Core handles routinely. The Photo(VehicleId,Stage,SequenceNumber)
            // index is unique specifically so the SECOND concurrent request's SaveChangesAsync fails
            // loudly instead of silently overwriting the first request's blobs — this loop is what
            // turns that loud failure into an invisible-to-the-user retry with a fresh number.
            // Blob writes are guarded the same way: they're create-only, so a colliding write also
            // fails loudly (409) rather than overwriting, and lands in this same catch. Both blob
            // uploads and the SaveChangesAsync sit inside one try per attempt precisely so either
            // symptom triggers the same cleanup-and-retry.
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var uploadedBlobPaths = new List<(string original, string? thumbnail)>();

                try
                {
                    var nextSequenceNumber = await _sequencer.NextSequenceNumberAsync(vehicle.Id, Stage);

                    foreach (var file in Files)
                    {
                        var sequenceNumber = nextSequenceNumber++;
                        var originalExtension = Path.GetExtension(file.FileName);
                        var originalFileName = PhotoNaming.BuildFileName(vehicle.Vin, vehicle.Reg, sequenceNumber, originalExtension);
                        var thumbnailFileName = PhotoNaming.BuildFileName(vehicle.Vin, vehicle.Reg, sequenceNumber, ".jpg");

                        var blobPathOriginal = $"{vehicle.BlobFolderName}/{Stage}/{originalFileName}";
                        var blobPathThumbnail = $"{vehicle.BlobFolderName}/{Stage}/{thumbnailFileName}";

                        using var buffered = new MemoryStream();
                        await using (var uploadStream = file.OpenReadStream())
                        {
                            await uploadStream.CopyToAsync(buffered);
                        }
                        buffered.Position = 0;

                        DateTime? dateTaken = DateTaken.HasValue
                            ? DateTime.SpecifyKind(DateTaken.Value, DateTimeKind.Utc)
                            : ExifDateReader.TryReadDateTaken(buffered);
                        buffered.Position = 0;

                        using var thumbnailStream = await ThumbnailGenerator.CreateThumbnailAsync(buffered);
                        buffered.Position = 0;

                        // Each blob path is recorded as SOON as its own upload succeeds — not batched
                        // after both original+thumbnail complete — so a failure between the two
                        // (e.g. the thumbnail upload throwing right after the original succeeded)
                        // still leaves the successfully-uploaded original in uploadedBlobPaths and
                        // therefore covered by cleanup below. Recording both together after the fact
                        // would silently orphan the original in that narrow case.
                        await _storage.UploadOriginalAsync(blobPathOriginal, buffered, file.ContentType);
                        uploadedBlobPaths.Add((blobPathOriginal, null));
                        await _storage.UploadThumbnailAsync(blobPathThumbnail, thumbnailStream, "image/jpeg");
                        uploadedBlobPaths[^1] = (blobPathOriginal, blobPathThumbnail);

                        _db.Photos.Add(new Photo
                        {
                            VehicleId = vehicle.Id,
                            Stage = Stage,
                            Part = Part,
                            FileName = originalFileName,
                            BlobPathOriginal = blobPathOriginal,
                            BlobPathThumbnail = blobPathThumbnail,
                            ContentType = file.ContentType,
                            SizeBytes = file.Length,
                            UploadedAtUtc = DateTime.UtcNow,
                            DateTakenUtc = dateTaken,
                            SequenceNumber = sequenceNumber,
                            UploaderId = userId
                        });
                    }

                    await _db.SaveChangesAsync();
                    break; // success — leave the retry loop
                }
                catch (Exception ex) when (IsSequenceConflict(ex) && attempt < maxAttempts)
                {
                    // Lost the race to a concurrent upload targeting the same vehicle+stage.
                    // Nothing committed to the DB (SaveChangesAsync itself failed) — clean up this
                    // attempt's blobs and any not-yet-flushed thumbnail path, then retry with a
                    // freshly-read sequence number.
                    await CleanUpBlobsAsync(uploadedBlobPaths);
                    _db.ChangeTracker.Clear(); // discard this attempt's tracked-but-unsaved Photo rows
                }
                catch
                {
                    // Any other failure (not a sequence conflict, or retries exhausted) — remove
                    // whatever blobs this attempt uploaded so storage doesn't silently accumulate
                    // untracked files that still cost money, then propagate.
                    await CleanUpBlobsAsync(uploadedBlobPaths);
                    throw;
                }
            }

            return RedirectToPage("/Vehicles/Details", new { id = vehicle.Id });
        }

        // Found via real concurrent-request testing (two Task.WhenAll'd POSTs racing the same
        // vehicle+stage): when two requests collide on the same sequence number, PhotoNaming
        // produces the IDENTICAL blob path for both — that's inherent to the path being a
        // deterministic function of (vehicle, stage, sequenceNumber, extension), not something
        // this fix changes. Naively deleting every path this attempt touched, unconditionally, is
        // unsafe: if the OTHER (winning) request's SaveChangesAsync already committed by the time
        // this one's cleanup runs, that path is now the winner's — deleting it destroys a live,
        // already-persisted Photo's file. Reproduced exactly: attempt A won with SequenceNumber=2,
        // attempt B lost, and B's unconditional cleanup deleted the blob A had just committed to,
        // leaving Photo row A in the DB pointing at a 404. Guarding the delete behind an existence
        // check closes that: if a Photo row now references this exact path, some other request
        // (almost certainly the one that caused this very failure) has already claimed it, so this
        // attempt leaves it alone — worst case a harmless untouched blob, never a deleted live one.
        //
        // The "wrong photo" race this guard alone did NOT close is now closed at the blob layer.
        // Blob uploads are create-only (IfNoneMatch = ETag.All in BlobPhotoStorage.UploadAsync), so
        // two requests can no longer both write bytes to the identical path: the second write is
        // rejected with 409 BlobAlreadyExists before it can land on top of the first. That used to
        // mean the winner's permanent Photo row could end up referencing the loser's image content,
        // silently and with nothing to detect it. Now the losing write is simply another conflict
        // IsSequenceConflict recognises, so it flows through this same cleanup-and-retry path and
        // comes back with a freshly-allocated sequence number and its own untouched blob path.
        //
        // One consequence worth knowing: because the loser is rejected BEFORE its upload call
        // returns, that path never enters uploadedBlobPaths, so there is correspondingly nothing to
        // clean up for it — only paths this attempt genuinely created are ever passed here.
        private async Task CleanUpBlobsAsync(List<(string original, string? thumbnail)> uploadedBlobPaths)
        {
            foreach (var (original, thumbnail) in uploadedBlobPaths)
            {
                var stillOrphaned = !await _db.Photos.AsNoTracking().AnyAsync(p => p.BlobPathOriginal == original);
                if (stillOrphaned)
                {
                    await _storage.DeleteOriginalAsync(original);
                    if (thumbnail is not null) await _storage.DeleteThumbnailAsync(thumbnail);
                }
            }
        }

        // Two different symptoms of the SAME underlying event — another concurrent request already
        // claimed this vehicle+stage+sequence number — and a retry with a fresh number fixes both:
        //
        //  1. The DB unique index rejecting SaveChangesAsync. EF Core wraps the provider's own
        //     constraint-violation exception in DbUpdateException, and which exception that is
        //     depends on which provider is actually running — this app uses SQLite locally and SQL
        //     Server in production (see Program.cs), and BOTH need recognising here or the retry
        //     this whole method exists for silently only works in dev:
        //       - SQLite: check the underlying SqliteException's EXTENDED error code
        //         (2067 = SQLITE_CONSTRAINT_UNIQUE) specifically — not just the primary code
        //         (19 = SQLITE_CONSTRAINT, which also covers foreign-key/not-null/check violations
        //         that should NOT be silently retried, since retrying wouldn't fix them).
        //       - SQL Server: check the underlying SqlException's Number for 2627 (violation of a
        //         PRIMARY KEY or UNIQUE KEY constraint) or 2601 (duplicate key row in a unique
        //         index) — the two error numbers SQL Server actually raises for this, depending on
        //         whether the index was declared as a constraint or a plain unique index.
        //  2. The blob layer rejecting a create-only upload because something is already at that
        //     path (see BlobPhotoStorage.UploadAsync). Matched with the same precision as the
        //     database checks — this exact status AND error code, not "any RequestFailedException"
        //     — so a 403, a 500, or a transport fault still propagates instead of quietly burning
        //     retry attempts on something a fresh sequence number cannot fix.
        //
        // Neither database check subsumes the other (only one provider is ever active in a given
        // environment), and the blob check subsumes neither: two colliding uploads with DIFFERENT
        // file extensions produce different blob paths for the same sequence number, so they sail
        // past the blob check and are caught only by (1); two with the same extension collide at
        // the blob layer first and are caught by (2) before any bytes can be overwritten.
        private static bool IsSequenceConflict(Exception ex)
        {
            if (ex is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 2067 } })
                return true;

            if (ex is DbUpdateException { InnerException: SqlException { Number: 2627 or 2601 } })
                return true;

            return ex is RequestFailedException { Status: 409 } blobConflict
                && blobConflict.ErrorCode == BlobErrorCode.BlobAlreadyExists.ToString();
        }
    }
}
