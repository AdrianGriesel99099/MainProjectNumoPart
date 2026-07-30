using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleDeletionServiceTests
    {
        private static VehicleDeletionService Build(AppDbContext db, FakePhotoStorage storage)
            => new(db, storage, NullLogger<VehicleDeletionService>.Instance);

        // Seeds a vehicle whose photos have blobs actually present in the fake storage, so a test
        // asserting "the blobs are gone" is asserting a real state change rather than a no-op.
        private static Vehicle SeedVehicle(AppDbContext db, FakePhotoStorage storage,
            string? vin, string? reg, int photoCount = 2)
        {
            var vehicle = new Vehicle
            {
                Vin = vin,
                Reg = reg,
                BlobFolderName = vin ?? reg!,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();

            for (var i = 1; i <= photoCount; i++)
            {
                var original = $"{vehicle.BlobFolderName}/Checkin/photo-{i}.jpg";
                var thumbnail = $"{vehicle.BlobFolderName}/Checkin/photo-{i}-thumb.jpg";

                storage.Originals[original] = new byte[] { 1, 2, 3 };
                storage.Thumbnails[thumbnail] = new byte[] { 4, 5, 6 };

                db.Photos.Add(new Photo
                {
                    VehicleId = vehicle.Id,
                    Stage = Stage.Checkin,
                    FileName = $"photo-{i}.jpg",
                    BlobPathOriginal = original,
                    BlobPathThumbnail = thumbnail,
                    ContentType = "image/jpeg",
                    SizeBytes = 3,
                    UploadedAtUtc = DateTime.UtcNow,
                    SequenceNumber = i,
                    UploaderId = "u1"
                });
            }
            db.SaveChanges();

            return vehicle;
        }

        [Fact]
        public async Task DeletesVehicleRowAndAllPhotoRows()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db, storage, "VIN123", "AB12CDE");

            var result = await Build(db, storage).DeleteAsync(vehicle.Id, "VIN123");

            Assert.Equal(VehicleDeleteStatus.Deleted, result.Status);
            Assert.Equal(2, result.PhotosDeleted);
            Assert.Empty(db.Vehicles);
            Assert.Empty(db.Photos);
        }

        [Fact]
        public async Task DeletesEveryOriginalAndThumbnailBlob()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db, storage, "VIN123", "AB12CDE", photoCount: 3);

            Assert.Equal(3, storage.Originals.Count);
            Assert.Equal(3, storage.Thumbnails.Count);

            await Build(db, storage).DeleteAsync(vehicle.Id, "VIN123");

            Assert.Empty(storage.Originals);
            Assert.Empty(storage.Thumbnails);
        }

        // The security-critical case: the typed confirmation is a server-side control, so a
        // mismatch must leave absolutely everything — rows AND blobs — untouched.
        [Fact]
        public async Task RejectsMismatchedConfirmationAndDeletesNothing()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db, storage, "VIN123", "AB12CDE");

            var result = await Build(db, storage).DeleteAsync(vehicle.Id, "WRONG");

            Assert.Equal(VehicleDeleteStatus.ConfirmationMismatch, result.Status);
            Assert.Single(db.Vehicles);
            Assert.Equal(2, db.Photos.Count());
            Assert.Equal(2, storage.Originals.Count);
            Assert.Equal(2, storage.Thumbnails.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task RejectsEmptyConfirmation(string? confirmation)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db, storage, "VIN123", "AB12CDE");

            var result = await Build(db, storage).DeleteAsync(vehicle.Id, confirmation);

            Assert.Equal(VehicleDeleteStatus.ConfirmationMismatch, result.Status);
            Assert.Single(db.Vehicles);
        }

        // Stored identifiers are normalized (upper-cased, whitespace stripped). Without matching
        // normalization on the typed value, nobody could ever confirm a spaced registration.
        [Theory]
        [InlineData("ab12cde")]
        [InlineData("AB12 CDE")]
        [InlineData("  ab12 cde  ")]
        public async Task AcceptsConfirmationIgnoringCaseAndWhitespace(string confirmation)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db, storage, null, "AB12CDE");

            var result = await Build(db, storage).DeleteAsync(vehicle.Id, confirmation);

            Assert.Equal(VehicleDeleteStatus.Deleted, result.Status);
        }

        [Fact]
        public async Task AcceptsEitherVinOrRegWhenBothPresent()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var byVin = SeedVehicle(db, storage, "VIN111", "AAA111");
            var byReg = SeedVehicle(db, storage, "VIN222", "BBB222");

            Assert.Equal(VehicleDeleteStatus.Deleted, (await Build(db, storage).DeleteAsync(byVin.Id, "VIN111")).Status);
            Assert.Equal(VehicleDeleteStatus.Deleted, (await Build(db, storage).DeleteAsync(byReg.Id, "BBB222")).Status);
        }

        [Fact]
        public async Task UsesRegWhenVinIsNull()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db, storage, null, "AB12CDE");

            var result = await Build(db, storage).DeleteAsync(vehicle.Id, "AB12CDE");

            Assert.Equal(VehicleDeleteStatus.Deleted, result.Status);
            Assert.Empty(db.Vehicles);
        }

        [Fact]
        public async Task ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();

            var result = await Build(db, storage).DeleteAsync(999999, "anything");

            Assert.Equal(VehicleDeleteStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task LeavesOtherVehiclesPhotosAndBlobsAlone()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var doomed = SeedVehicle(db, storage, "VIN111", "AAA111");
            var keeper = SeedVehicle(db, storage, "VIN222", "BBB222");

            await Build(db, storage).DeleteAsync(doomed.Id, "VIN111");

            Assert.Single(db.Vehicles);
            Assert.Equal(keeper.Id, db.Vehicles.Single().Id);
            Assert.Equal(2, db.Photos.Count());
            Assert.All(storage.Originals.Keys, k => Assert.StartsWith("VIN222/", k));
            Assert.All(storage.Thumbnails.Keys, k => Assert.StartsWith("VIN222/", k));
        }
    }
}
