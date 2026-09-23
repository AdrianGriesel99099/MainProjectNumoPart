using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Services
{
    public enum PhotoDownloadStatus
    {
        Success,
        NotFound
    }

    public record PhotoDownloadResult(
        PhotoDownloadStatus Status, byte[]? Content = null, string? ContentType = null, string? FileName = null);

    // A single photo's original bytes under its friendly on-disk name (Photo.FileName), separate
    // from the multi-select zip/export actions — for grabbing just the one image someone is
    // already looking at without wading through a selection flow first.
    public class PhotoDownloadService
    {
        private readonly AppDbContext _db;
        private readonly IPhotoStorage _storage;

        public PhotoDownloadService(AppDbContext db, IPhotoStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        public async Task<PhotoDownloadResult> GetAsync(int photoId, CancellationToken ct = default)
        {
            var photo = await _db.Photos.FindAsync(new object[] { photoId }, ct);
            if (photo is null)
            {
                return new PhotoDownloadResult(PhotoDownloadStatus.NotFound);
            }

            await using var stream = await _storage.OpenOriginalReadAsync(photo.BlobPathOriginal, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);

            return new PhotoDownloadResult(PhotoDownloadStatus.Success, buffer.ToArray(), photo.ContentType, photo.FileName);
        }
    }
}
