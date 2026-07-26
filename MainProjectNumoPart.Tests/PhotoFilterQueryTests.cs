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
    }
}
