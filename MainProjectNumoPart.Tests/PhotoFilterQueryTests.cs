using System;
using System.Linq;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoFilterQueryTests
    {
        private static void SeedPhoto(Data.AppDbContext db, Vehicle vehicle, Stage stage, DateTime uploaded, DateTime? taken, int sequence)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = stage, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = uploaded, DateTakenUtc = taken, SequenceNumber = sequence, UploaderId = "u1"
            });
        }

        [Fact]
        public void Apply_FiltersByStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, null, 1);
            SeedPhoto(db, vehicle, Stage.Quote, DateTime.UtcNow, null, 1);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { Stage = Stage.Checkin }).ToList();

            Assert.Single(result);
            Assert.Equal(Stage.Checkin, result[0].Stage);
        }

        [Fact]
        public void Apply_FiltersByVinOrReg()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            var v2 = new Vehicle { Vin = "V2", BlobFolderName = "V2", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.AddRange(v1, v2);
            db.SaveChanges();
            SeedPhoto(db, v1, Stage.Checkin, DateTime.UtcNow, null, 1);
            SeedPhoto(db, v2, Stage.Checkin, DateTime.UtcNow, null, 1);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { VinOrReg = "V1" }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_FiltersByUploadedDateRange()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, 1);
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null, 2);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                UploadedFrom = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
        }

        // A VIN/reg typed in a different case or with different internal spacing is the SAME
        // vehicle to a human. SQLite's default collation is case-sensitive, so this only passes
        // because the filter normalizes its term the same way the lookup/create path does.
        [Theory]
        [InlineData("ab12cde")]
        [InlineData("AB12 CDE")]
        [InlineData("  ab12 cde  ")]
        public void Apply_FiltersByVinOrReg_IsCaseAndWhitespaceInsensitive(string searchTerm)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var match = new Vehicle { Reg = "AB12CDE", BlobFolderName = "AB12CDE", CreatedAtUtc = DateTime.UtcNow };
            var other = new Vehicle { Reg = "ZZ99ZZZ", BlobFolderName = "ZZ99ZZZ", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.AddRange(match, other);
            db.SaveChanges();
            SeedPhoto(db, match, Stage.Checkin, DateTime.UtcNow, null, 1);
            SeedPhoto(db, other, Stage.Checkin, DateTime.UtcNow, null, 1);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { VinOrReg = searchTerm }).ToList();

            Assert.Single(result);
            Assert.Equal(match.Id, result[0].VehicleId);
        }

        // The bug this pins: an <input type="date"> binds to MIDNIGHT at the start of the chosen
        // day, so a `<= bound` comparison excluded everything uploaded during the end date itself.
        [Fact]
        public void Apply_FiltersByUploadedTo_IncludesPhotoUploadedOnTheEndDateItself()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            // Uploaded partway through 1 June — exactly the row the old `<=` bound silently dropped.
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 1, 14, 30, 0, DateTimeKind.Utc), null, 1);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                UploadedTo = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_FiltersByUploadedTo_ExcludesPhotoUploadedAfterTheEndDate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 1, 23, 59, 59, DateTimeKind.Utc), null, 1);
            // The very start of the next day must fall OUTSIDE a "through 1 June" filter.
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), null, 2);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                UploadedTo = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
            Assert.Equal(1, result[0].SequenceNumber);
        }

        [Fact]
        public void Apply_FiltersByTakenTo_IncludesPhotoTakenOnTheEndDateItself()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow,
                new DateTime(2026, 6, 1, 14, 30, 0, DateTimeKind.Utc), 1);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                TakenTo = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_FiltersByTakenTo_ExcludesNullsAndPhotosTakenAfterTheEndDate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow,
                new DateTime(2026, 6, 1, 23, 59, 59, DateTimeKind.Utc), 1);
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow,
                new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), 2);
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, null, 3); // no EXIF date — must be excluded
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                TakenTo = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
            Assert.Equal(1, result[0].SequenceNumber);
        }

        [Fact]
        public void Apply_FiltersByTakenDateRange_ExcludingNulls()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1);
            SeedPhoto(db, vehicle, Stage.Checkin, DateTime.UtcNow, null, 2); // no EXIF date — must be excluded

            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter
            {
                TakenFrom = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_WithNoFilters_ReturnsAllOrderedByUploadedDescending()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, 1);
            SeedPhoto(db, vehicle, Stage.Checkin, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null, 2);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter()).ToList();

            Assert.Equal(2, result.Count);
            Assert.True(result[0].UploadedAtUtc > result[1].UploadedAtUtc);
        }

        private static void SeedTaggedPhoto(Data.AppDbContext db, Vehicle vehicle, Part? part, int sequence)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, Part = part, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = sequence, UploaderId = "u1"
            });
        }

        [Fact]
        public void Apply_FiltersByPart()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedTaggedPhoto(db, vehicle, Part.FrontBumper, 1);
            SeedTaggedPhoto(db, vehicle, Part.Roof, 2);
            SeedTaggedPhoto(db, vehicle, null, 3);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { Part = Part.FrontBumper }).ToList();

            Assert.Single(result);
            Assert.Equal(Part.FrontBumper, result[0].Part);
        }

        // This is the worklist for tag-after-upload: without it there is no way to find the
        // photos that still need attention once a batch has been uploaded untagged.
        [Fact]
        public void Apply_UntaggedOnly_ReturnsExactlyTheNullPartRows()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedTaggedPhoto(db, vehicle, Part.FrontBumper, 1);
            SeedTaggedPhoto(db, vehicle, null, 2);
            SeedTaggedPhoto(db, vehicle, null, 3);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(db.Photos, new PhotoFilter { UntaggedOnly = true }).ToList();

            Assert.Equal(2, result.Count);
            Assert.All(result, p => Assert.Null(p.Part));
        }

        // UntaggedOnly must win over a stray Part value rather than the two combining into an
        // impossible "untagged AND FrontBumper" query that returns nothing.
        [Fact]
        public void Apply_UntaggedOnlyTakesPrecedenceOverPart()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedTaggedPhoto(db, vehicle, null, 1);
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(
                db.Photos, new PhotoFilter { UntaggedOnly = true, Part = Part.FrontBumper }).ToList();

            Assert.Single(result);
        }

        [Fact]
        public void Apply_PartCombinesWithStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, Part = Part.Roof, FileName = "a.jpg",
                BlobPathOriginal = "o1", BlobPathThumbnail = "t1", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1"
            });
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Quote, Part = Part.Roof, FileName = "b.jpg",
                BlobPathOriginal = "o2", BlobPathThumbnail = "t2", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 2, UploaderId = "u1"
            });
            db.SaveChanges();

            var result = PhotoFilterQuery.Apply(
                db.Photos, new PhotoFilter { Part = Part.Roof, Stage = Stage.Checkin }).ToList();

            Assert.Single(result);
            Assert.Equal(Stage.Checkin, result[0].Stage);
        }
    }
}
