using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Photos;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotosIndexModelTests
    {
        private static void SeedPhoto(Data.AppDbContext db, Vehicle vehicle, int sequence)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o" + sequence, BlobPathThumbnail = "t" + sequence, ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = sequence, UploaderId = "u1"
            });
        }

        [Fact]
        public async Task OnGetAsync_WithFewMatches_ReportsExactTotalAndIsNotTruncated()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            for (var i = 1; i <= 3; i++) SeedPhoto(db, vehicle, i);
            db.SaveChanges();

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(3, model.Photos.Count);
            Assert.Equal(3, model.TotalMatchCount);
            Assert.False(model.IsTruncated);
        }

        // The bug this pins: filtering to more than the display cap used to silently drop the
        // extra rows with no indication anything was cut off. Staff filtering a busy month would
        // see a plausible-looking grid that was actually missing photos.
        [Fact]
        public async Task OnGetAsync_WithMoreMatchesThanTheDisplayCap_TruncatesButReportsTheRealTotal()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            for (var i = 1; i <= 125; i++) SeedPhoto(db, vehicle, i);
            db.SaveChanges();

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(120, model.Photos.Count);
            Assert.Equal(125, model.TotalMatchCount);
            Assert.True(model.IsTruncated);
        }

        [Fact]
        public async Task OnGetAsync_TotalMatchCount_RespectsActiveFilters()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, 1);
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Quote, FileName = "f.jpg",
                BlobPathOriginal = "o2", BlobPathThumbnail = "t2", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 2, UploaderId = "u1"
            });
            db.SaveChanges();

            var model = new IndexModel(db) { Stage = Stage.Checkin };
            await model.OnGetAsync();

            Assert.Single(model.Photos);
            Assert.Equal(1, model.TotalMatchCount);
            Assert.False(model.IsTruncated);
        }
    }
}
