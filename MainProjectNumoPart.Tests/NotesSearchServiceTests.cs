using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class NotesSearchServiceTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin, string? reg = null)
        {
            var v = new Vehicle { Vin = vin, Reg = reg, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static Photo SeedPhoto(Data.AppDbContext db, Vehicle vehicle, int sequence)
        {
            var p = new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = sequence, UploaderId = "u1"
            };
            db.Photos.Add(p);
            db.SaveChanges();
            return p;
        }

        [Fact]
        public async Task FindsMatchingVehicleUpdateCaseInsensitively()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN001");
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = v.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "Replaced the RUSTED sill panel", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync("rusted");

            var result = Assert.Single(results);
            Assert.Equal(NoteSearchKind.VehicleUpdate, result.Kind);
            Assert.Equal(v.Id, result.VehicleId);
            Assert.Null(result.PhotoId);
            Assert.Contains("RUSTED", result.Snippet);
        }

        [Fact]
        public async Task FindsMatchingPhotoCommentAndCarriesItsVehicleAndPhotoId()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN002");
            var photo = SeedPhoto(db, v, 1);
            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photo.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "customer asked about the scratch near the mirror", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync("scratch");

            var result = Assert.Single(results);
            Assert.Equal(NoteSearchKind.PhotoComment, result.Kind);
            Assert.Equal(v.Id, result.VehicleId);
            Assert.Equal(photo.Id, result.PhotoId);
        }

        [Fact]
        public async Task FindsMatchingDamageMarkNote()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN003");
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = v.Id, Part = Part.FrontBumper, PhotoId = null,
                XPercent = 50, YPercent = 50, Note = "Deep dent near the fog light",
                AuthorId = "u1", AuthorEmail = "a@example.com", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync("dent");

            var result = Assert.Single(results);
            Assert.Equal(NoteSearchKind.DamageMark, result.Kind);
            Assert.Equal(v.Id, result.VehicleId);
        }

        [Fact]
        public async Task OrdersAllKindsTogetherNewestFirst()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN004");
            var photo = SeedPhoto(db, v, 1);
            var now = DateTime.UtcNow;
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = v.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "brake noise reported", CreatedAtUtc = now.AddMinutes(-10)
            });
            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photo.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "brake dust visible here", CreatedAtUtc = now
            });
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = v.Id, Part = Part.FrontBumper, PhotoId = null,
                XPercent = 50, YPercent = 50, Note = "brake line clipped bumper edge",
                AuthorId = "u1", AuthorEmail = "a@example.com", CreatedAtUtc = now.AddMinutes(-5)
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync("brake");

            Assert.Equal(3, results.Count);
            Assert.Equal(NoteSearchKind.PhotoComment, results[0].Kind);
            Assert.Equal(NoteSearchKind.DamageMark, results[1].Kind);
            Assert.Equal(NoteSearchKind.VehicleUpdate, results[2].Kind);
        }

        [Fact]
        public async Task NonMatchingQueryReturnsNoResults()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN005");
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = v.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "oil changed", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync("gearbox");

            Assert.Empty(results);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("a")]
        public async Task TooShortOrEmptyQueryReturnsNoResultsWithoutSearching(string? query)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN006");
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = v.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "a note", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync(query);

            Assert.Empty(results);
        }

        [Fact]
        public async Task ResultCarriesVehicleIdentifiersForLinking()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db, "VIN007", reg: "AB12CDE");
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = v.Id, AuthorId = "u1", AuthorEmail = "tech@example.com",
                Body = "clutch replaced under warranty", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var results = await new NotesSearchService(db).SearchAsync("clutch");

            var result = Assert.Single(results);
            Assert.Equal("VIN007", result.VehicleVin);
            Assert.Equal("AB12CDE", result.VehicleReg);
            Assert.Equal("tech@example.com", result.AuthorEmail);
        }
    }
}
