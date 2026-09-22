using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers the "All vehicles" browse page's "Needs attention" filter -- narrows the list down to
    // vehicles whose last activity (per VehicleLastActivity) is stale, so a lead can see forgotten
    // jobs without scanning the "Stale" badge column page by page.
    public class VehiclesIndexModelStaleOnlyTests
    {
        private static readonly DateTime StaleCutoff = DateTime.UtcNow - Services.VehicleLastActivity.StaleThreshold;

        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin, DateTime createdAtUtc)
        {
            var vehicle = new Vehicle
            {
                Vin = vin,
                BlobFolderName = vin,
                CreatedAtUtc = createdAtUtc
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            return vehicle;
        }

        private static void AddPhoto(Data.AppDbContext db, int vehicleId, DateTime uploadedAtUtc)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicleId, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = Guid.NewGuid().ToString(), BlobPathThumbnail = Guid.NewGuid().ToString(),
                ContentType = "image/jpeg", UploadedAtUtc = uploadedAtUtc, SequenceNumber = 1,
                UploaderId = "u1"
            });
            db.SaveChanges();
        }

        private static void AddVehicleUpdate(Data.AppDbContext db, int vehicleId, DateTime createdAtUtc)
        {
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = vehicleId, AuthorId = "u1", AuthorEmail = "u1@example.com",
                Body = "note", CreatedAtUtc = createdAtUtc
            });
            db.SaveChanges();
        }

        private static void AddDamageMark(Data.AppDbContext db, int vehicleId, DateTime createdAtUtc)
        {
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = vehicleId, Part = Part.FrontBumper, XPercent = 50, YPercent = 50,
                Note = "n", AuthorId = "u1", AuthorEmail = "u1@example.com", CreatedAtUtc = createdAtUtc
            });
            db.SaveChanges();
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyFalse_ReturnsAllVehicles()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-1));
            SeedVehicle(db, "VIN2", DateTime.UtcNow);

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(2, model.Vehicles.Count);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_ExcludesVehiclesCreatedRecently()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var stale = SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-1));
            var fresh = SeedVehicle(db, "VIN2", DateTime.UtcNow);

            var model = new IndexModel(db) { StaleOnly = true };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(stale.Id, model.Vehicles[0].Id);
            Assert.Equal(1, model.TotalCount);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_ExcludesVehiclesWithRecentPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var oldWithRecentPhoto = SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-30));
            AddPhoto(db, oldWithRecentPhoto.Id, DateTime.UtcNow);

            var model = new IndexModel(db) { StaleOnly = true };
            await model.OnGetAsync();

            Assert.Empty(model.Vehicles);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_ExcludesVehiclesWithRecentVehicleUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var oldWithRecentUpdate = SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-30));
            AddVehicleUpdate(db, oldWithRecentUpdate.Id, DateTime.UtcNow);

            var model = new IndexModel(db) { StaleOnly = true };
            await model.OnGetAsync();

            Assert.Empty(model.Vehicles);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_ExcludesVehiclesWithRecentDamageMark()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var oldWithRecentMark = SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-30));
            AddDamageMark(db, oldWithRecentMark.Id, DateTime.UtcNow);

            var model = new IndexModel(db) { StaleOnly = true };
            await model.OnGetAsync();

            Assert.Empty(model.Vehicles);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_IncludesVehiclesWithOnlyOldActivity()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var stale = SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-30));
            AddPhoto(db, stale.Id, StaleCutoff.AddDays(-10));
            AddVehicleUpdate(db, stale.Id, StaleCutoff.AddDays(-20));
            AddDamageMark(db, stale.Id, StaleCutoff.AddDays(-15));

            var model = new IndexModel(db) { StaleOnly = true };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(stale.Id, model.Vehicles[0].Id);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_CombinesWithSearch()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var matchStale = SeedVehicle(db, "MATCHVIN1", StaleCutoff.AddDays(-1));
            var matchFresh = SeedVehicle(db, "MATCHVIN2", DateTime.UtcNow);
            var nonMatchStale = SeedVehicle(db, "OTHER1", StaleCutoff.AddDays(-2));

            var model = new IndexModel(db) { Search = "MATCH", StaleOnly = true };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(matchStale.Id, model.Vehicles[0].Id);
        }

        [Fact]
        public async Task OnGetAsync_WithStaleOnlyTrue_AndResultsBeyondLastPage_ClampsToLastPage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var stale = SeedVehicle(db, "VIN1", StaleCutoff.AddDays(-1));
            SeedVehicle(db, "VIN2", DateTime.UtcNow);

            var model = new IndexModel(db) { StaleOnly = true, PageNumber = 5 };
            await model.OnGetAsync();

            Assert.Equal(1, model.TotalPages);
            Assert.Equal(1, model.PageNumber);
            Assert.Single(model.Vehicles);
        }
    }
}
