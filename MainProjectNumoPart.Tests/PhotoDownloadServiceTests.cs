using System;
using System.IO;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoDownloadServiceTests
    {
        private static PhotoDownloadService Build(AppDbContext db, FakePhotoStorage storage)
            => new(db, storage);

        private static Vehicle SeedVehicle(AppDbContext db, string reg = "AB12CDE")
        {
            var v = new Vehicle { Reg = reg, BlobFolderName = reg, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static Photo SeedPhoto(
            AppDbContext db, FakePhotoStorage storage, Vehicle vehicle, byte[] bytes,
            string fileName = "AB12CDE-Reg-001.jpg", string contentType = "image/jpeg")
        {
            var blobPath = $"{vehicle.BlobFolderName}/Checkin/{fileName}";
            storage.Originals[blobPath] = bytes;

            var photo = new Photo
            {
                VehicleId = vehicle.Id,
                Stage = Stage.Checkin,
                FileName = fileName,
                BlobPathOriginal = blobPath,
                BlobPathThumbnail = blobPath + "-t",
                ContentType = contentType,
                SizeBytes = bytes.Length,
                UploadedAtUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                UploaderId = "u1"
            };
            db.Photos.Add(photo);
            db.SaveChanges();
            return photo;
        }

        [Fact]
        public async Task GetAsync_UnknownId_ReturnsNotFound()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();

            var result = await Build(db, storage).GetAsync(999);

            Assert.Equal(PhotoDownloadStatus.NotFound, result.Status);
            Assert.Null(result.Content);
        }

        [Fact]
        public async Task GetAsync_ExistingPhoto_ReturnsOriginalBytesWithContentTypeAndFriendlyFileName()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            var photo = SeedPhoto(db, storage, vehicle, bytes, fileName: "AB12CDE-Reg-001.jpg", contentType: "image/jpeg");

            var result = await Build(db, storage).GetAsync(photo.Id);

            Assert.Equal(PhotoDownloadStatus.Success, result.Status);
            Assert.Equal(bytes, result.Content);
            Assert.Equal("image/jpeg", result.ContentType);
            Assert.Equal("AB12CDE-Reg-001.jpg", result.FileName);
        }

        [Fact]
        public async Task GetAsync_PngPhoto_KeepsOriginalContentTypeAndExtension_NoReencoding()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var bytes = new byte[] { 9, 9, 9 };
            var photo = SeedPhoto(db, storage, vehicle, bytes, fileName: "AB12CDE-Reg-002.png", contentType: "image/png");

            var result = await Build(db, storage).GetAsync(photo.Id);

            Assert.Equal(PhotoDownloadStatus.Success, result.Status);
            Assert.Equal(bytes, result.Content);
            Assert.Equal("image/png", result.ContentType);
            Assert.Equal("AB12CDE-Reg-002.png", result.FileName);
        }
    }
}
