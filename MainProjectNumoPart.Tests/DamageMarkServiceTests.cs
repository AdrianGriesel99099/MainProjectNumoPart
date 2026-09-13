using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class DamageMarkServiceTests
    {
        private static DamageMarkService Build(Data.AppDbContext db)
            => new(db, NullLogger<DamageMarkService>.Instance);

        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin = "VIN123")
        {
            var v = new Vehicle { Vin = vin, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static Photo SeedPhoto(Data.AppDbContext db, Vehicle vehicle, Part? part = null)
        {
            var p = new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, Part = part, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1"
            };
            db.Photos.Add(p);
            db.SaveChanges();
            return p;
        }

        [Fact]
        public async Task AddAsync_CreatesAPositionOnlyMarkWhenNoPhotoGiven()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            var result = await Build(db).AddAsync(
                vehicle.Id, Part.FrontBumper, null, 42.5, 60.0, "Scratch on the corner", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.Success, result.Status);
            var saved = db.DamageMarks.Single();
            Assert.Equal(Part.FrontBumper, saved.Part);
            Assert.Null(saved.PhotoId);
            Assert.Equal(42.5, saved.XPercent);
            Assert.Equal(60.0, saved.YPercent);
            Assert.Equal("staff@w.local", saved.AuthorEmail);
        }

        [Fact]
        public async Task AddAsync_CreatesAPhotoAnchoredMark()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            var photo = SeedPhoto(db, vehicle, Part.DoorFrontLeft);

            var result = await Build(db).AddAsync(
                vehicle.Id, Part.DoorFrontLeft, photo.Id, 10, 20, "Dent", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Equal(photo.Id, db.DamageMarks.Single().PhotoId);
        }

        // The part is captured on the mark directly rather than inferred from Photo.Part, so a
        // later re-tag of the photo can't silently change what an existing mark says it's about.
        [Fact]
        public async Task PartStaysStableEvenIfPhotoIsLaterRetagged()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            var photo = SeedPhoto(db, vehicle, Part.DoorFrontLeft);
            await Build(db).AddAsync(vehicle.Id, Part.DoorFrontLeft, photo.Id, 10, 20, "Dent", "u1", "staff@w.local");

            photo.Part = Part.DoorFrontRight;
            db.SaveChanges();

            Assert.Equal(Part.DoorFrontLeft, db.DamageMarks.Single().Part);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task AddAsync_RejectsEmptyNote(string? note)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            var result = await Build(db).AddAsync(vehicle.Id, Part.FrontBumper, null, 50, 50, note, "u1", "staff@w.local");

            Assert.Equal(NoteStatus.EmptyBody, result.Status);
            Assert.Empty(db.DamageMarks);
        }

        [Fact]
        public async Task AddAsync_RejectsNoteOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            var tooLong = new string('x', DamageMarkService.MaxNoteLength + 1);

            var result = await Build(db).AddAsync(vehicle.Id, Part.FrontBumper, null, 50, 50, tooLong, "u1", "staff@w.local");

            Assert.Equal(NoteStatus.TooLong, result.Status);
            Assert.Empty(db.DamageMarks);
        }

        [Fact]
        public async Task AddAsync_ReturnsNotFoundForUnknownVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).AddAsync(999, Part.FrontBumper, null, 50, 50, "text", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        // Defends against a stale or tampered client payload: a photoId that genuinely exists
        // but belongs to a DIFFERENT vehicle must not silently anchor a mark to the wrong car.
        [Fact]
        public async Task AddAsync_RejectsAPhotoBelongingToADifferentVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicleA = SeedVehicle(db, "VINA");
            var vehicleB = SeedVehicle(db, "VINB");
            var photoOnB = SeedPhoto(db, vehicleB);

            var result = await Build(db).AddAsync(
                vehicleA.Id, Part.FrontBumper, photoOnB.Id, 50, 50, "text", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.NotFound, result.Status);
            Assert.Empty(db.DamageMarks);
        }

        // canvasWrap (wwwroot/css: .damage-mark-canvas-wrap) has overflow:hidden, so a pin outside
        // 0-100 on either axis renders clipped -- invisible and unclickable -- even though the note
        // itself would still save. Clamping here keeps every mark reachable through its pin instead
        // of only through the DamageMarksNewestFirst list.
        [Theory]
        [InlineData(150.0, 50.0, 100.0, 50.0)]
        [InlineData(-30.0, 50.0, 0.0, 50.0)]
        [InlineData(50.0, 150.0, 50.0, 100.0)]
        [InlineData(50.0, -30.0, 50.0, 0.0)]
        [InlineData(0.0, 100.0, 0.0, 100.0)]
        public async Task AddAsync_ClampsOutOfRangePositionsIntoZeroToHundred(
            double xPercent, double yPercent, double expectedX, double expectedY)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            var result = await Build(db).AddAsync(
                vehicle.Id, Part.FrontBumper, null, xPercent, yPercent, "Scuff", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.Success, result.Status);
            var saved = db.DamageMarks.Single();
            Assert.Equal(expectedX, saved.XPercent);
            Assert.Equal(expectedY, saved.YPercent);
        }

        [Fact]
        public async Task AddAsync_TreatsNonFinitePositionsAsCentred()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            var result = await Build(db).AddAsync(
                vehicle.Id, Part.FrontBumper, null, double.NaN, double.PositiveInfinity, "Scuff", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.Success, result.Status);
            var saved = db.DamageMarks.Single();
            Assert.Equal(50.0, saved.XPercent);
            Assert.Equal(50.0, saved.YPercent);
        }

        [Fact]
        public async Task DeleteAsync_RemovesTheMark()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, Part.FrontBumper, null, 50, 50, "text", "u1", "staff@w.local");
            var id = db.DamageMarks.Single().Id;

            var result = await Build(db).DeleteAsync(id);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.DamageMarks);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).DeleteAsync(999);

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        // Photo -> DamageMark is Restrict, not Cascade, at the DB level — a second DB-enforced
        // cascade path into DamageMarks alongside Vehicle->DamageMark is exactly what SQL Server
        // refuses to create the table over ("may cause cycles or multiple cascade paths"), even
        // though SQLite allows it. Restrict also turns off EF's own CLIENT-SIDE cascade fixup for
        // tracked entities (Cascade does both DB- and client-side; Restrict does neither) — so a
        // raw EF remove of a photo no longer takes its anchored mark down with it, proving cleanup
        // is no longer automatic and that PhotoEndpoints' explicit "delete the marks, then the
        // photo" ordering is load-bearing, not an optional nicety copied from elsewhere.
        [Fact]
        public async Task DeletingAPhotoNoLongerAutomaticallyRemovesItsAnchoredMark()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            var photo = SeedPhoto(db, vehicle);
            await Build(db).AddAsync(vehicle.Id, Part.FrontBumper, photo.Id, 50, 50, "text", "u1", "staff@w.local");

            db.Photos.Remove(photo);
            db.SaveChanges();

            Assert.Single(db.DamageMarks);
        }

        // A position-only mark never referenced a photo, so deleting an UNRELATED photo on
        // the same vehicle must leave it untouched.
        [Fact]
        public async Task DeletingAnUnrelatedPhotoLeavesPositionOnlyMarksAlone()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            var photo = SeedPhoto(db, vehicle);
            await Build(db).AddAsync(vehicle.Id, Part.FrontBumper, null, 50, 50, "position-only mark", "u1", "staff@w.local");

            db.Photos.Remove(photo);
            db.SaveChanges();

            Assert.Single(db.DamageMarks);
        }

        [Fact]
        public async Task MarkSurvivesIndependentlyOfAuthorAccount()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            await Build(db).AddAsync(vehicle.Id, Part.FrontBumper, null, 50, 50, "text", "deleted-user-id", "gone@w.local");

            Assert.Equal("gone@w.local", db.DamageMarks.Single().AuthorEmail);
        }
    }
}
