using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Services;

namespace MainProjectNumoPart.Tests
{
    public class FakePhotoStorage : IPhotoStorage
    {
        public Dictionary<string, byte[]> Originals { get; } = new();
        public Dictionary<string, byte[]> Thumbnails { get; } = new();

        public async Task UploadOriginalAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => Originals[blobPath] = await ReadAllAsync(content, ct);

        public async Task UploadThumbnailAsync(string blobPath, Stream content, string contentType, CancellationToken ct = default)
            => Thumbnails[blobPath] = await ReadAllAsync(content, ct);

        private static async Task<byte[]> ReadAllAsync(Stream content, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            return ms.ToArray();
        }

        public Task DeleteOriginalAsync(string blobPath, CancellationToken ct = default)
        {
            Originals.Remove(blobPath);
            return Task.CompletedTask;
        }

        public Task DeleteThumbnailAsync(string blobPath, CancellationToken ct = default)
        {
            Thumbnails.Remove(blobPath);
            return Task.CompletedTask;
        }

        public Task<Stream> OpenOriginalReadAsync(string blobPath, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(Originals[blobPath]));

        public Task<Uri> GetThumbnailReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => Task.FromResult(new Uri($"https://fake.local/thumbnails/{blobPath}"));

        public Task<Uri> GetOriginalReadUrlAsync(string blobPath, TimeSpan validFor, CancellationToken ct = default)
            => Task.FromResult(new Uri($"https://fake.local/originals/{blobPath}"));
    }
}
