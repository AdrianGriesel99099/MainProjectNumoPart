using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    public enum PhotoTagStatus
    {
        Updated,
        NoPhotosSelected,
        TooManySelected
    }

    public record PhotoTagResult(PhotoTagStatus Status, int Updated = 0, string? Message = null);

    // Applies a Part to a batch of already-uploaded photos. Separate from upload on purpose:
    // the real workflow is shooting a burst of photos and sorting them afterwards, so tagging
    // has to work on photos that already exist rather than only as a field on the upload form.
    public class PhotoTaggingService
    {
        // Matches the cap on the zip-download endpoint. A tag request is far cheaper than a zip,
        // but the same ceiling keeps the two bulk actions predictable and stops an unbounded
        // id list turning into an unbounded UPDATE.
        public const int MaxPhotosPerRequest = 200;

        private readonly AppDbContext _db;
        private readonly ILogger<PhotoTaggingService> _logger;

        public PhotoTaggingService(AppDbContext db, ILogger<PhotoTaggingService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // part == null clears the tag. That is deliberate and is how a mis-tag gets undone —
        // without it the only way back would be deleting and re-uploading the photo.
        public async Task<PhotoTagResult> SetPartAsync(
            IReadOnlyList<int> photoIds, Part? part, string userId, CancellationToken ct = default)
        {
            if (photoIds is null || photoIds.Count == 0)
            {
                return new PhotoTagResult(PhotoTagStatus.NoPhotosSelected, 0, "No photos selected.");
            }

            if (photoIds.Count > MaxPhotosPerRequest)
            {
                return new PhotoTagResult(PhotoTagStatus.TooManySelected, 0,
                    $"Too many photos selected. Maximum {MaxPhotosPerRequest} at a time.");
            }

            var distinctIds = photoIds.Distinct().ToList();

            var photos = await _db.Photos
                .Where(p => distinctIds.Contains(p.Id))
                .ToListAsync(ct);

            // Ids that no longer exist are ignored rather than failing the batch: a photo can be
            // deleted between the page rendering and the tag being applied, and refusing the
            // whole request over one stale id would be worse than tagging the rest.
            foreach (var photo in photos)
            {
                photo.Part = part;
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Part tagging: {Count} photo(s) set to {Part} by {UserId}",
                photos.Count, part?.ToString() ?? "(none)", userId);

            return new PhotoTagResult(PhotoTagStatus.Updated, photos.Count);
        }
    }
}
