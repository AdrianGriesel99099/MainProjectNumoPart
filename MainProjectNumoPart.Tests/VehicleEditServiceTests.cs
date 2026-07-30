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
    public class VehicleEditServiceTests
    {
        private static VehicleEditService Build(AppDbContext db)
            => new(db, NullLogger<VehicleEditService>.Instance);

        private static Vehicle Seed(AppDbContext db, string? vin, string? reg, string? makeModel = null)
        {
            var vehicle = new Vehicle
            {
                Vin = vin,
                Reg = reg,
                MakeModel = makeModel,
                BlobFolderName = vin ?? reg!,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            return vehicle;
        }

        [Fact]
        public async Task UpdatesAllThreeFields()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "OLDVIN1", "OLD111");

            var result = await Build(db).UpdateAsync(vehicle.Id, "NEWVIN1", "NEW222", "Toyota Hilux", "u1");

            Assert.Equal(VehicleEditStatus.Updated, result.Status);
            var saved = db.Vehicles.Single();
            Assert.Equal("NEWVIN1", saved.Vin);
            Assert.Equal("NEW222", saved.Reg);
            Assert.Equal("Toyota Hilux", saved.MakeModel);
        }

        // The whole point of the edit page for this field: MakeModel has no other way to be set.
        [Fact]
        public async Task SetsMakeModelOnAVehicleThatHadNone()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");
            Assert.Null(vehicle.MakeModel);

            await Build(db).UpdateAsync(vehicle.Id, "VIN123", "AB12CDE", "Ford Ranger", "u1");

            Assert.Equal("Ford Ranger", db.Vehicles.Single().MakeModel);
        }

        // BlobFolderName is the storage key every existing photo's path was built from. Changing it
        // would orphan every image the vehicle already has.
        [Fact]
        public async Task NeverChangesBlobFolderNameEvenWhenVinChanges()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "ORIGINALVIN", null);
            var originalFolder = vehicle.BlobFolderName;

            await Build(db).UpdateAsync(vehicle.Id, "COMPLETELYNEWVIN", null, null, "u1");

            Assert.Equal(originalFolder, db.Vehicles.Single().BlobFolderName);
        }

        [Theory]
        [InlineData("ab12 cde", "AB12CDE")]
        [InlineData("  vin 123  ", "VIN123")]
        public async Task NormalizesIdentifiersOnSave(string typed, string expected)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "SEEDVIN", null);

            await Build(db).UpdateAsync(vehicle.Id, typed, null, null, "u1");

            Assert.Equal(expected, db.Vehicles.Single().Vin);
        }

        [Fact]
        public async Task RejectsVinAlreadyUsedByAnotherVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var target = Seed(db, "MYVIN", "MYREG");
            Seed(db, "TAKENVIN", "OTHERREG");

            var result = await Build(db).UpdateAsync(target.Id, "TAKENVIN", "MYREG", null, "u1");

            Assert.Equal(VehicleEditStatus.VinConflict, result.Status);
            Assert.Equal("MYVIN", db.Vehicles.Single(v => v.Id == target.Id).Vin);
        }

        [Fact]
        public async Task RejectsRegAlreadyUsedByAnotherVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var target = Seed(db, "MYVIN", "MYREG");
            Seed(db, "OTHERVIN", "TAKENREG");

            var result = await Build(db).UpdateAsync(target.Id, "MYVIN", "TAKENREG", null, "u1");

            Assert.Equal(VehicleEditStatus.RegConflict, result.Status);
            Assert.Equal("MYREG", db.Vehicles.Single(v => v.Id == target.Id).Reg);
        }

        // Re-saving an unchanged form must not collide with the vehicle's own identifiers.
        [Fact]
        public async Task AllowsSavingItsOwnUnchangedIdentifiers()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");

            var result = await Build(db).UpdateAsync(vehicle.Id, "VIN123", "AB12CDE", "Mazda", "u1");

            Assert.Equal(VehicleEditStatus.Updated, result.Status);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "  ")]
        public async Task RejectsClearingBothIdentifiers(string? vin, string? reg)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");

            var result = await Build(db).UpdateAsync(vehicle.Id, vin, reg, null, "u1");

            Assert.Equal(VehicleEditStatus.NoIdentifier, result.Status);
            var saved = db.Vehicles.Single();
            Assert.Equal("VIN123", saved.Vin);
            Assert.Equal("AB12CDE", saved.Reg);
        }

        [Fact]
        public async Task AllowsClearingOneIdentifierWhenTheOtherRemains()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");

            var result = await Build(db).UpdateAsync(vehicle.Id, "VIN123", null, null, "u1");

            Assert.Equal(VehicleEditStatus.Updated, result.Status);
            Assert.Null(db.Vehicles.Single().Reg);
        }

        [Fact]
        public async Task BlankMakeModelIsStoredAsNullNotEmptyString()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", null, "Toyota");

            await Build(db).UpdateAsync(vehicle.Id, "VIN123", null, "   ", "u1");

            Assert.Null(db.Vehicles.Single().MakeModel);
        }

        [Fact]
        public async Task ReturnsNotFoundForUnknownVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).UpdateAsync(999999, "VIN", null, null, "u1");

            Assert.Equal(VehicleEditStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task LeavesPhotosAttachedAfterVinChange()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "OLDVIN", null);
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id,
                Stage = Stage.Checkin,
                FileName = "p.jpg",
                BlobPathOriginal = "OLDVIN/Checkin/p.jpg",
                BlobPathThumbnail = "OLDVIN/Checkin/p-thumb.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 1,
                UploadedAtUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                UploaderId = "u1"
            });
            db.SaveChanges();

            await Build(db).UpdateAsync(vehicle.Id, "BRANDNEWVIN", null, null, "u1");

            var photo = db.Photos.Single();
            Assert.Equal(vehicle.Id, photo.VehicleId);
            // Existing blob paths are untouched — they still point at the original folder.
            Assert.Equal("OLDVIN/Checkin/p.jpg", photo.BlobPathOriginal);
        }
    }
}
