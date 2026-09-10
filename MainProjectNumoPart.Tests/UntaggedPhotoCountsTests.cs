using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class UntaggedPhotoCountsTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin)
        {
            var v = new Vehicle { Vin = vin, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static void SeedPhoto(Data.AppDbContext db, Vehicle vehicle, Part? part, int sequence)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, Part = part, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = sequence, UploaderId = "u1"
            });
        }

        [Fact]
        public async Task CountsOnlyUntaggedPhotosPerVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1");
            var v2 = SeedVehicle(db, "V2");
            db.SaveChanges();
            SeedPhoto(db, v1, null, 1);
            SeedPhoto(db, v1, null, 2);
            SeedPhoto(db, v1, Part.FrontBumper, 3);
            SeedPhoto(db, v2, Part.Roof, 1);
            db.SaveChanges();

            var counts = await UntaggedPhotoCounts.ForVehiclesAsync(db, new[] { v1.Id, v2.Id });

            Assert.Equal(2, counts[v1.Id]);
            Assert.False(counts.ContainsKey(v2.Id));
        }

        [Fact]
        public async Task VehicleWithNoUntaggedPhotosIsAbsentFromTheResult()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1");
            db.SaveChanges();
            SeedPhoto(db, v1, Part.Bonnet, 1);
            db.SaveChanges();

            var counts = await UntaggedPhotoCounts.ForVehiclesAsync(db, new[] { v1.Id });

            Assert.False(counts.ContainsKey(v1.Id));
        }

        [Fact]
        public async Task VehicleWithNoPhotosAtAllIsAbsentFromTheResult()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1");
            db.SaveChanges();

            var counts = await UntaggedPhotoCounts.ForVehiclesAsync(db, new[] { v1.Id });

            Assert.Empty(counts);
        }

        [Fact]
        public async Task EmptyVehicleIdListReturnsEmptyWithoutQuerying()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var counts = await UntaggedPhotoCounts.ForVehiclesAsync(db, Array.Empty<int>());

            Assert.Empty(counts);
        }

        // A vehicle appearing in both the "recently added" and "search results" lists on the
        // home page must only be counted once, not have its photos double-counted.
        [Fact]
        public async Task DuplicateVehicleIdsAreCountedOnce()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1");
            db.SaveChanges();
            SeedPhoto(db, v1, null, 1);
            db.SaveChanges();

            var counts = await UntaggedPhotoCounts.ForVehiclesAsync(db, new[] { v1.Id, v1.Id });

            Assert.Equal(1, counts[v1.Id]);
        }

        [Fact]
        public async Task DoesNotCountAnotherVehiclesPhotosAgainstThisOne()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "V1");
            var v2 = SeedVehicle(db, "V2");
            db.SaveChanges();
            SeedPhoto(db, v1, null, 1);
            SeedPhoto(db, v2, null, 1);
            SeedPhoto(db, v2, null, 2);
            db.SaveChanges();

            var counts = await UntaggedPhotoCounts.ForVehiclesAsync(db, new[] { v1.Id, v2.Id });

            Assert.Equal(1, counts[v1.Id]);
            Assert.Equal(2, counts[v2.Id]);
        }
    }
}
