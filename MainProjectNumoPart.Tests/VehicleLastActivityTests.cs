using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleLastActivityTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin, DateTime createdAtUtc)
        {
            var v = new Vehicle { Vin = vin, BlobFolderName = vin, CreatedAtUtc = createdAtUtc };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static int _photoSequence;

        private static void SeedPhoto(Data.AppDbContext db, Vehicle vehicle, DateTime uploadedAtUtc)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o" + Interlocked.Increment(ref _photoSequence),
                BlobPathThumbnail = "t" + _photoSequence, ContentType = "image/jpeg",
                UploadedAtUtc = uploadedAtUtc, SequenceNumber = _photoSequence, UploaderId = "u1"
            });
            db.SaveChanges();
        }

        private static void SeedUpdate(Data.AppDbContext db, Vehicle vehicle, DateTime createdAtUtc)
        {
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = vehicle.Id, AuthorId = "u1", AuthorEmail = "u1@example.com",
                Body = "note", CreatedAtUtc = createdAtUtc
            });
            db.SaveChanges();
        }

        private static void SeedDamageMark(Data.AppDbContext db, Vehicle vehicle, DateTime createdAtUtc)
        {
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = vehicle.Id, Part = Part.FrontBumper, XPercent = 50, YPercent = 50,
                Note = "dent", AuthorId = "u1", AuthorEmail = "u1@example.com", CreatedAtUtc = createdAtUtc
            });
            db.SaveChanges();
        }

        [Fact]
        public async Task VehicleWithNoActivityFallsBackToCreatedAtUtc()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var createdAt = DateTime.UtcNow.AddDays(-10);
            var vehicle = SeedVehicle(db, "V1", createdAt);

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(createdAt, activity[vehicle.Id]);
        }

        [Fact]
        public async Task LastActivityIsMostRecentPhotoUpload()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1", DateTime.UtcNow.AddDays(-10));
            SeedPhoto(db, vehicle, DateTime.UtcNow.AddDays(-3));
            var latest = DateTime.UtcNow.AddDays(-1);
            SeedPhoto(db, vehicle, latest);

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(latest, activity[vehicle.Id]);
        }

        [Fact]
        public async Task LastActivityIsMostRecentJobCardUpdateWhenNewerThanPhotos()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1", DateTime.UtcNow.AddDays(-10));
            SeedPhoto(db, vehicle, DateTime.UtcNow.AddDays(-5));
            var latest = DateTime.UtcNow.AddDays(-1);
            SeedUpdate(db, vehicle, latest);

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(latest, activity[vehicle.Id]);
        }

        [Fact]
        public async Task LastActivityIsMostRecentDamageMarkWhenNewerThanPhotosAndUpdates()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1", DateTime.UtcNow.AddDays(-10));
            SeedPhoto(db, vehicle, DateTime.UtcNow.AddDays(-5));
            SeedUpdate(db, vehicle, DateTime.UtcNow.AddDays(-4));
            var latest = DateTime.UtcNow.AddDays(-1);
            SeedDamageMark(db, vehicle, latest);

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(latest, activity[vehicle.Id]);
        }

        [Fact]
        public async Task PicksMaxAcrossAllThreeActivitySources()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1", DateTime.UtcNow.AddDays(-30));
            SeedUpdate(db, vehicle, DateTime.UtcNow.AddDays(-6));
            var latest = DateTime.UtcNow.AddDays(-2);
            SeedPhoto(db, vehicle, latest);
            SeedDamageMark(db, vehicle, DateTime.UtcNow.AddDays(-8));

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, new[] { vehicle.Id });

            Assert.Equal(latest, activity[vehicle.Id]);
        }

        [Fact]
        public async Task EmptyVehicleIdListReturnsEmptyWithoutQuerying()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, Array.Empty<int>());

            Assert.Empty(activity);
        }

        [Fact]
        public async Task DoesNotMixUpDifferentVehiclesActivity()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1", DateTime.UtcNow.AddDays(-20));
            var v2 = SeedVehicle(db, "V2", DateTime.UtcNow.AddDays(-20));
            var v1Latest = DateTime.UtcNow.AddDays(-5);
            var v2Latest = DateTime.UtcNow.AddDays(-1);
            SeedPhoto(db, v1, v1Latest);
            SeedPhoto(db, v2, v2Latest);

            var activity = await VehicleLastActivity.ForVehiclesAsync(db, new[] { v1.Id, v2.Id });

            Assert.Equal(v1Latest, activity[v1.Id]);
            Assert.Equal(v2Latest, activity[v2.Id]);
        }

        [Theory]
        [InlineData(7 * 24 * 60 * 60 - 1, false)]
        [InlineData(7 * 24 * 60 * 60, false)]
        [InlineData(7 * 24 * 60 * 60 + 1, true)]
        public void IsStale_UsesSevenDayThreshold(int secondsAgo, bool expectedStale)
        {
            var now = DateTime.UtcNow;
            var lastActivity = now.AddSeconds(-secondsAgo);

            Assert.Equal(expectedStale, VehicleLastActivity.IsStale(lastActivity, now));
        }
    }
}
