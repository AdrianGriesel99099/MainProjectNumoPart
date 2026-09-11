using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace MainProjectNumoPart.Services
{
    public enum PhotoExportFormat
    {
        Pdf,
        Zip
    }

    public enum PhotoExportStatus
    {
        Success,
        NoPhotosSelected,
        TooManySelected,
        TooLarge
    }

    public record PhotoExportResult(
        PhotoExportStatus Status, byte[]? Content = null, string? ContentType = null,
        string? FileName = null, string? Message = null);

    // Builds a downloadable PDF report or annotated zip for a staff-picked set of photos, so a
    // client can be handed a clean record of a vehicle's photos (with stage/part/date and its
    // comment thread) without screenshotting or forwarding raw uploads. Never sends anything
    // itself — the caller downloads the file and attaches it wherever they already message the
    // client.
    public class PhotoExportService
    {
        // Same ceiling as the plain bulk-download endpoint (PhotoEndpoints./download) — export
        // does strictly more work per photo (re-encoding, possibly pin-burning, PDF layout), so
        // reusing rather than loosening that cap keeps this bulk action just as predictable.
        public const int MaxPhotosPerRequest = 200;
        public const long MaxTotalOriginalBytes = 500L * 1024 * 1024;

        private readonly AppDbContext _db;
        private readonly IPhotoStorage _storage;
        private readonly ILogger<PhotoExportService> _logger;

        public PhotoExportService(AppDbContext db, IPhotoStorage storage, ILogger<PhotoExportService> logger)
        {
            _db = db;
            _storage = storage;
            _logger = logger;
        }

        public async Task<PhotoExportResult> ExportAsync(
            IReadOnlyList<int> photoIds, PhotoExportFormat format, bool includeDamageMarks, CancellationToken ct = default)
        {
            if (photoIds is null || photoIds.Count == 0)
            {
                return new PhotoExportResult(PhotoExportStatus.NoPhotosSelected, Message: "No photos selected.");
            }

            if (photoIds.Count > MaxPhotosPerRequest)
            {
                return new PhotoExportResult(PhotoExportStatus.TooManySelected, Message:
                    $"Too many photos selected. Maximum {MaxPhotosPerRequest} at a time.");
            }

            var distinctIds = photoIds.Distinct().ToList();

            // Ids that no longer exist are dropped rather than failing the whole export — same
            // reasoning as the tagging/download endpoints: a photo can be deleted between page
            // render and export.
            var photos = await _db.Photos
                .Include(p => p.Vehicle)
                .Where(p => distinctIds.Contains(p.Id))
                .ToListAsync(ct);

            if (photos.Count == 0)
            {
                return new PhotoExportResult(PhotoExportStatus.NoPhotosSelected, Message: "No photos selected.");
            }

            var totalBytes = photos.Sum(p => p.SizeBytes);
            if (totalBytes > MaxTotalOriginalBytes)
            {
                return new PhotoExportResult(PhotoExportStatus.TooLarge, Message:
                    $"Selected photos total {totalBytes / (1024 * 1024)}MB, which exceeds the " +
                    $"{MaxTotalOriginalBytes / (1024 * 1024)}MB export limit. Select fewer photos.");
            }

            // Same order the vehicle page groups photos in: by stage, then by shoot order within it.
            photos = photos.OrderBy(p => (int)p.Stage).ThenBy(p => p.SequenceNumber).ToList();
            var orderedIds = photos.Select(p => p.Id).ToList();

            var commentsByPhoto = (await _db.PhotoComments
                    .Where(c => orderedIds.Contains(c.PhotoId))
                    .OrderBy(c => c.CreatedAtUtc)
                    .ToListAsync(ct))
                .GroupBy(c => c.PhotoId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var marksByPhoto = includeDamageMarks
                ? (await _db.DamageMarks
                        .Where(m => m.PhotoId != null && orderedIds.Contains(m.PhotoId!.Value))
                        .ToListAsync(ct))
                    .GroupBy(m => m.PhotoId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList())
                : new Dictionary<int, List<DamageMark>>();

            var items = new List<PhotoExportItem>(photos.Count);
            foreach (var photo in photos)
            {
                ct.ThrowIfCancellationRequested();

                await using var originalStream = await _storage.OpenOriginalReadAsync(photo.BlobPathOriginal, ct);
                using var buffer = new MemoryStream();
                await originalStream.CopyToAsync(buffer, ct);
                var originalBytes = buffer.ToArray();

                var marks = marksByPhoto.TryGetValue(photo.Id, out var m) ? m : new List<DamageMark>();
                byte[] finalBytes;
                if (marks.Count > 0)
                {
                    var pins = marks.Select(mk => (mk.XPercent, mk.YPercent)).ToList();
                    finalBytes = await DamageMarkImageAnnotator.BurnPinsAsync(originalBytes, pins, ct);
                }
                else if (!string.Equals(photo.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    // "a zip of JPEGs" — normalize every non-JPEG source (PNG uploads) so every
                    // file in the export is actually a .jpg, regardless of what was uploaded.
                    finalBytes = await NormalizeToJpegAsync(originalBytes, ct);
                }
                else
                {
                    finalBytes = originalBytes;
                }

                var comments = commentsByPhoto.TryGetValue(photo.Id, out var cs) ? cs : new List<PhotoComment>();

                items.Add(new PhotoExportItem(
                    BuildFileNameBase(photo),
                    finalBytes,
                    photo.Stage,
                    photo.Part,
                    photo.UploadedAtUtc,
                    comments.Select(c => new PhotoExportComment(c.AuthorEmail, c.CreatedAtUtc, c.Body)).ToList()));
            }

            byte[] content;
            string contentType;
            string extension;
            if (format == PhotoExportFormat.Pdf)
            {
                content = PhotoExportPdfBuilder.Build(items);
                contentType = "application/pdf";
                extension = "pdf";
            }
            else
            {
                content = PhotoExportZipBuilder.Build(items);
                contentType = "application/zip";
                extension = "zip";
            }

            var vehicle = photos[0].Vehicle;
            var vehicleLabel = SanitizeForFileName(vehicle?.Reg ?? vehicle?.Vin ?? "vehicle");
            var fileName = $"photos-{vehicleLabel}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.{extension}";

            _logger.LogInformation(
                "Exported {Count} photo(s) as {Format} (damage marks {Marks})",
                items.Count, format, includeDamageMarks ? "included" : "excluded");

            return new PhotoExportResult(PhotoExportStatus.Success, content, contentType, fileName);
        }

        private static async Task<byte[]> NormalizeToJpegAsync(byte[] bytes, CancellationToken ct)
        {
            using var image = await Image.LoadAsync<Rgba32>(new MemoryStream(bytes), ct);
            using var output = new MemoryStream();
            await image.SaveAsync(output, new JpegEncoder { Quality = 90 }, ct);
            return output.ToArray();
        }

        private static string BuildFileNameBase(Photo photo)
        {
            var partLabel = photo.Part.HasValue ? photo.Part.Value.ToString() : "Untagged";
            return $"{photo.Stage}_{partLabel}_{photo.SequenceNumber:000}";
        }

        private static readonly Regex UnsafeFileNameChars = new("[^A-Za-z0-9_-]+", RegexOptions.Compiled);

        private static string SanitizeForFileName(string value)
        {
            var cleaned = UnsafeFileNameChars.Replace(value, "-").Trim('-');
            return cleaned.Length == 0 ? "vehicle" : cleaned;
        }
    }
}
