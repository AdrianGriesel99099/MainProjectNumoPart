using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MainProjectNumoPart.Services
{
    public interface IPhotoStorage
    {
        Task UploadOriginalAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default);
        Task UploadThumbnailAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default);
        Task DeleteOriginalAsync(string blobPath, CancellationToken ct = default);
        Task DeleteThumbnailAsync(string blobPath, CancellationToken ct = default);
        Task<Stream> OpenOriginalReadAsync(string blobPath, CancellationToken ct = default);
        Task<Uri> GetThumbnailReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default);
        Task<Uri> GetOriginalReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default);
    }
}
