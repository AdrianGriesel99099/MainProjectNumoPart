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

            var uploadedBlobPaths = new List<(string original, string thumbnail)>();

            try
            {
                // Sequence numbers are allocated once per batch, then handed out locally as
                // sequenceNumber++. NextSequenceNumberAsync queries the Photos table directly, and
                // the Photo rows for files already processed earlier in THIS loop are only added to
                // the change tracker, not yet saved (SaveChangesAsync runs once, after the loop) — so
                // calling it again per file would not see those pending rows and would keep handing
                // back the same number, producing duplicate blob paths that silently overwrite each
                // other. A single allocation upfront (safe under the same single-instance assumption
                // documented on PhotoSequenceAllocator) avoids that.
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

                    await _storage.UploadOriginalAsync(blobPathOriginal, buffered, file.ContentType);
                    await _storage.UploadThumbnailAsync(blobPathThumbnail, thumbnailStream, "image/jpeg");
                    uploadedBlobPaths.Add((blobPathOriginal, blobPathThumbnail));

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
            }
            catch
            {
                // The DB save (or a later blob write in the loop) failed after some blobs
                // already landed — remove them so storage doesn't silently accumulate
                // untracked files that still cost money.
                foreach (var (original, thumbnail) in uploadedBlobPaths)
                {
                    await _storage.DeleteOriginalAsync(original);
                    await _storage.DeleteThumbnailAsync(thumbnail);
                }
                throw;
            }

            return RedirectToPage("/Vehicles/Details", new { id = vehicle.Id });
        }
    }
}
