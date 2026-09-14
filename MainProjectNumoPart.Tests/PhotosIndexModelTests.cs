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

        [Fact]
        public async Task OnGetAsync_WithNoDateFilters_HasNoDateRangeWarnings()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Empty(model.DateRangeWarnings);
        }

        [Fact]
        public async Task OnGetAsync_WithValidUploadedRange_HasNoDateRangeWarnings()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var model = new IndexModel(db)
            {
                UploadedFrom = new DateTime(2026, 1, 1),
                UploadedTo = new DateTime(2026, 1, 31)
            };
            await model.OnGetAsync();

            Assert.Empty(model.DateRangeWarnings);
        }

        // The bug this pins: swapping the 'from' and 'to' dates by mistake used to just filter
        // out every photo with no indication why -- an empty grid looks identical whether the
        // vehicle genuinely has nothing in range or the dates were entered backwards.
        [Fact]
        public async Task OnGetAsync_WithUploadedFromAfterUploadedTo_ReportsWarningAndFindsNothing()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            SeedPhoto(db, vehicle, 1);
            db.SaveChanges();

            var model = new IndexModel(db)
            {
                UploadedFrom = new DateTime(2026, 1, 31),
                UploadedTo = new DateTime(2026, 1, 1)
            };
            await model.OnGetAsync();

            Assert.Equal(0, model.TotalMatchCount);
            var warning = Assert.Single(model.DateRangeWarnings);
            Assert.Contains("Uploaded", warning);
        }

        [Fact]
        public async Task OnGetAsync_WithTakenFromAfterTakenTo_ReportsWarning()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var model = new IndexModel(db)
            {
                TakenFrom = new DateTime(2026, 1, 31),
                TakenTo = new DateTime(2026, 1, 1)
            };
            await model.OnGetAsync();

            var warning = Assert.Single(model.DateRangeWarnings);
            Assert.Contains("Taken", warning);
        }

        [Fact]
        public async Task OnGetAsync_WithBothRangesInverted_ReportsBothWarnings()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var model = new IndexModel(db)
            {
                UploadedFrom = new DateTime(2026, 1, 31),
                UploadedTo = new DateTime(2026, 1, 1),
                TakenFrom = new DateTime(2026, 2, 28),
                TakenTo = new DateTime(2026, 2, 1)
            };
            await model.OnGetAsync();

            Assert.Equal(2, model.DateRangeWarnings.Count);
        }

        // Same date for 'from' and 'to' is a valid one-day range, not an inversion.
        [Fact]
        public async Task OnGetAsync_WithEqualFromAndToDates_HasNoDateRangeWarnings()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var model = new IndexModel(db)
            {
                UploadedFrom = new DateTime(2026, 1, 15),
                UploadedTo = new DateTime(2026, 1, 15)
            };
            await model.OnGetAsync();

            Assert.Empty(model.DateRangeWarnings);
        }
    }
}
