using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class MyActivityServiceTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin, string? reg = null)
        {
            var v = new Vehicle { Vin = vin, Reg = reg, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static Photo SeedPhoto(Data.AppDbContext db, Vehicle vehicle, string uploaderId, DateTime uploadedAtUtc,
            Stage stage = Stage.Checkin, int sequence = 1)
        {
            var p = new Photo
            {
                VehicleId = vehicle.Id, Stage = stage, FileName = "f.jpg",
                BlobPathOriginal = "o" + Guid.NewGuid(), BlobPathThumbnail = "t" + Guid.NewGuid(),
                ContentType = "image/jpeg", UploadedAtUtc = uploadedAtUtc, SequenceNumber = sequence,
                UploaderId = uploaderId
            };
            db.Photos.Add(p);
            db.SaveChanges();
            return p;
        }

        [Fact]
        public async Task IncludesPhotoUploadsByThatUserWithinTheWindowGroupedByVehicleAndStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1", "AB12CDE");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            SeedPhoto(db, vehicle, "u1", today, Stage.Checkin, 1);
            SeedPhoto(db, vehicle, "u1", today.AddMinutes(2), Stage.Checkin, 2);
            SeedPhoto(db, vehicle, "u1", today.AddMinutes(3), Stage.Checkin, 3);

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            var upload = Assert.Single(items, i => i.Kind == MyActivityKind.PhotoUpload);
            Assert.Equal(vehicle.Id, upload.VehicleId);
            Assert.Equal("AB12CDE", upload.VehicleLabel);
            Assert.Contains("3", upload.Summary);
            Assert.Contains("Checkin", upload.Summary);
        }

        [Fact]
        public async Task SeparatesUploadGroupsByStageForTheSameVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            SeedPhoto(db, vehicle, "u1", today, Stage.Checkin, 1);
            SeedPhoto(db, vehicle, "u1", today, Stage.Quote, 1);

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            Assert.Equal(2, items.Count(i => i.Kind == MyActivityKind.PhotoUpload));
        }

        [Fact]
        public async Task ExcludesUploadsByAnotherUser()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            SeedPhoto(db, vehicle, "otherUser", today, Stage.Checkin, 1);

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            Assert.Empty(items);
        }

        [Fact]
        public async Task ExcludesUploadsOutsideTheWindow()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            SeedPhoto(db, vehicle, "u1", today.AddDays(-1), Stage.Checkin, 1);
            SeedPhoto(db, vehicle, "u1", today.AddDays(1), Stage.Checkin, 2);

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            Assert.Empty(items);
        }

        [Fact]
        public async Task IncludesVehicleUpdatesByThatUserWithinTheWindow()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1", "AB12CDE");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = vehicle.Id, AuthorId = "u1", AuthorEmail = "u1@example.com",
                Body = "Replaced the front bumper clips.", CreatedAtUtc = today
            });
            db.SaveChanges();

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            var item = Assert.Single(items);
            Assert.Equal(MyActivityKind.VehicleUpdate, item.Kind);
            Assert.Equal(vehicle.Id, item.VehicleId);
            Assert.Contains("Replaced the front bumper clips.", item.Summary);
        }

        [Fact]
        public async Task IncludesPhotoCommentsByThatUserWithinTheWindowAndLinksThePhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            var photo = SeedPhoto(db, vehicle, "someoneElse", today.AddDays(-3), Stage.Checkin, 1);
            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photo.Id, AuthorId = "u1", AuthorEmail = "u1@example.com",
                Body = "Scratch confirmed pre-existing.", CreatedAtUtc = today
            });
            db.SaveChanges();

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            var item = Assert.Single(items);
            Assert.Equal(MyActivityKind.PhotoComment, item.Kind);
            Assert.Equal(photo.Id, item.PhotoId);
            Assert.Contains("Scratch confirmed pre-existing.", item.Summary);
        }

        [Fact]
        public async Task IncludesDamageMarksByThatUserWithinTheWindow()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = vehicle.Id, Part = Part.FrontBumper, PhotoId = null,
                XPercent = 50, YPercent = 50, Note = "Dent near the fog light.",
                AuthorId = "u1", AuthorEmail = "u1@example.com", CreatedAtUtc = today
            });
            db.SaveChanges();

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            var item = Assert.Single(items);
            Assert.Equal(MyActivityKind.DamageMark, item.Kind);
            Assert.Contains("FrontBumper", item.Summary);
            Assert.Contains("Dent near the fog light.", item.Summary);
        }

        [Fact]
        public async Task OrdersAllKindsByTimestampDescending()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "V1");
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
            SeedPhoto(db, vehicle, "u1", today, Stage.Checkin, 1);
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = vehicle.Id, AuthorId = "u1", AuthorEmail = "u1@example.com",
                Body = "Later update.", CreatedAtUtc = today.AddHours(1)
            });
            db.SaveChanges();

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            Assert.Equal(MyActivityKind.VehicleUpdate, items[0].Kind);
            Assert.Equal(MyActivityKind.PhotoUpload, items[1].Kind);
        }

        [Fact]
        public async Task ReturnsEmptyWhenTheUserHadNoActivityInTheWindow()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var today = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

            var items = await MyActivityService.ForUserAsync(db, "u1", today.Date, today.Date.AddDays(1));

            Assert.Empty(items);
        }
    }
}
