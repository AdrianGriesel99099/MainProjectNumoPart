// Requires Azurite running locally (see Task 5, Step 2). If Azurite isn't running,
// these tests fail with a connection error, not a false pass.
//
// Version-skew note: with a sufficiently new Azure.Storage.Blobs client and an older
// Azurite, requests can fail with 400 "The API version ... is not supported by Azurite"
// instead of a connection error. If you see that, start Azurite with
// --skipApiVersionCheck (or upgrade Azurite) rather than downgrading the NuGet package.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class BlobPhotoStorageTests
    {
        private static BlobPhotoStorage CreateStorage()
        {
            var client = new BlobServiceClient("UseDevelopmentStorage=true");
            return new BlobPhotoStorage(client);
        }

        [Fact]
        public async Task UploadAndOpenOriginal_RoundTrips()
        {
            var storage = CreateStorage();
            var path = $"test-vehicle/Checkin/roundtrip-{Guid.NewGuid()}.jpg";
            var content = Encoding.UTF8.GetBytes("fake jpeg bytes");

            await storage.UploadOriginalAsync(path, new MemoryStream(content), "image/jpeg");

            await using var readBack = await storage.OpenOriginalReadAsync(path);
            using var ms = new MemoryStream();
            await readBack.CopyToAsync(ms);

            Assert.Equal(content, ms.ToArray());
        }

        [Fact]
        public async Task DeleteOriginal_RemovesBlob()
        {
            var storage = CreateStorage();
            var path = $"test-vehicle/Checkin/delete-{Guid.NewGuid()}.jpg";
            await storage.UploadOriginalAsync(path, new MemoryStream(Encoding.UTF8.GetBytes("x")), "image/jpeg");

            await storage.DeleteOriginalAsync(path);

            await Assert.ThrowsAnyAsync<Exception>(() => storage.OpenOriginalReadAsync(path));
        }

        [Fact]
        public async Task GetThumbnailReadUrlAsync_ProducesAWorkingSasUrl()
        {
            var storage = CreateStorage();
            var path = $"test-vehicle/Checkin/sas-{Guid.NewGuid()}.jpg";
            var content = Encoding.UTF8.GetBytes("thumbnail bytes");
            await storage.UploadThumbnailAsync(path, new MemoryStream(content), "image/jpeg");

            var url = await storage.GetThumbnailReadUrlAsync(path, TimeSpan.FromMinutes(15));

            using var http = new System.Net.Http.HttpClient();
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var downloaded = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(content, downloaded);
        }
    }
}
