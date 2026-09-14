using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleCurrentStageTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin)
        {
            var v = new Vehicle { Vin = vin, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static void SeedPhoto(
            Data.AppDbContext db, Vehicle vehicle, Stage stage, DateTime uploadedAtUtc, int sequence)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = stage, FileName = "f.jpg",
                BlobPathOriginal = "o" + sequence, BlobPathThumbnail = "t" + sequence, ContentType = "image/jpeg",
                UploadedAtUtc = uploadedAtUtc, SequenceNumber = sequence, UploaderId = "u1"
            });
        }

        [Fact]
        public async Task VehiclesCurrentStageIsItsMostRecentlyUploadedPhotosStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow.AddDays(-3), 1);
            SeedPhoto(db, vehicle, Stage.Progress, DateTime.UtcNow.AddDays(-1), 2);
            SeedPhoto(db, vehicle, Stage.Quote, DateTime.UtcNow.AddDays(-2), 3);
            db.SaveChanges();

            var stages = await VehicleCurrentStage.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(Stage.Progress, stages[vehicle.Id]);
        }

        // Bulk uploads can land in the same second, so upload timestamp alone doesn't always
        // order them -- the higher sequence number is the tie-breaker for "shot last".
        [Fact]
        public async Task WhenUploadedAtTiesTheHigherSequenceNumberWins()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            db.SaveChanges();
            var sameInstant = DateTime.UtcNow;
            SeedPhoto(db, vehicle, Stage.Checkin, sameInstant, 1);
            SeedPhoto(db, vehicle, Stage.Checkout, sameInstant, 2);
            db.SaveChanges();

            var stages = await VehicleCurrentStage.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(Stage.Checkout, stages[vehicle.Id]);
        }

        [Fact]
        public async Task VehicleWithNoPhotosIsAbsentFromTheResult()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            db.SaveChanges();

            var stages = await VehicleCurrentStage.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.False(stages.ContainsKey(vehicle.Id));
        }

        [Fact]
        public async Task EmptyVehicleIdListReturnsEmptyWithoutQuerying()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var stages = await VehicleCurrentStage.ForVehiclesAsync(db, Array.Empty<int>());

            Assert.Empty(stages);
        }

        [Fact]
        public async Task DoesNotMixUpDifferentVehiclesStages()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1");
            var v2 = SeedVehicle(db, "V2");
            db.SaveChanges();
            SeedPhoto(db, v1, Stage.Checkin, DateTime.UtcNow.AddDays(-1), 1);
            SeedPhoto(db, v2, Stage.Extra, DateTime.UtcNow.AddDays(-1), 1);
            db.SaveChanges();

            var stages = await VehicleCurrentStage.ForVehiclesAsync(db, new[] { v1.Id, v2.Id });

            Assert.Equal(Stage.Checkin, stages[v1.Id]);
            Assert.Equal(Stage.Extra, stages[v2.Id]);
        }
    }
}
