// Pages/Upload.cshtml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages
{
    [Authorize]
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
        [BindProperty] public DateTime? DateTaken { get; set; }
        [BindProperty] public List<IFormFile> Files { get; set; } = new();

        public string? ErrorMessage { get; set; }

        public void OnGet(int? vehicleId)
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
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
                vehicle = await _vehicles.FindOrCreateAsync(Vin, Reg);
            }
            catch (VehicleIdentifierConflictException ex)
            {
                // VIN and Reg point at two different existing vehicles — almost always a typo.
                // No blobs have been touched yet, so nothing to clean up here.
                ErrorMessage = ex.Message;
                return Page();
            }
            await _db.SaveChangesAsync(); // assigns vehicle.Id before it's used in blob paths below

            // Retries up to 3 times total. Guards against a race the first version of this method
            // didn't: two concurrent uploads to the SAME vehicle+stage can both read the same
            // "next" sequence number before either commits — "single instance" (Global Constraints)
            // rules out multiple *replicas*, not multiple *concurrent requests* within the one
            // process, which ASP.NET Core handles routinely. The Photo(VehicleId,Stage,SequenceNumber)
            // index is unique specifically so the SECOND concurrent request's SaveChangesAsync fails
            // loudly instead of silently overwriting the first request's blobs — this loop is what
            // turns that loud failure into an invisible-to-the-user retry with a fresh number.
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
        // Known residual gap, not closed by this guard, flagged rather than silently fixed: blob
        // uploads themselves aren't isolated per attempt — both requests can genuinely write bytes
        // to the identical path before either commits. If the LOSING request's write physically
        // lands after the WINNING request's, the winner's now-permanent Photo row can end up
        // pointing at the loser's image content instead of its own (wrong photo, not a missing
        // one). Fully closing that needs a real design choice (e.g. per-attempt staging paths
        // finalized only after a successful commit, or conditional/ETag-guarded blob uploads that
        // reject an overwrite and turn this into another catchable, retryable conflict) — left for
        // that decision rather than invented here.
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

        // EF Core wraps SQLite constraint violations in DbUpdateException; check the underlying
        // SqliteException's EXTENDED error code (2067 = SQLITE_CONSTRAINT_UNIQUE) specifically —
        // not just the primary code (19 = SQLITE_CONSTRAINT, which also covers foreign-key/not-null/
        // check violations that should NOT be silently retried, since retrying wouldn't fix them.
        private static bool IsSequenceConflict(Exception ex)
        {
            return ex is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 2067 } };
        }
    }
}
