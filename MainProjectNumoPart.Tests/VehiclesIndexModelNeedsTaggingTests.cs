using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers the "All vehicles" browse page's "Needs tagging" filter -- narrows the list down to
    // vehicles with at least one untagged photo, so staff can find the day's tagging backlog
    // without paging through every vehicle looking for the warning badge.
    public class VehiclesIndexModelNeedsTaggingTests
    {
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

        private static void AddPhoto(Data.AppDbContext db, int vehicleId, Part? part)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicleId, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = Guid.NewGuid().ToString(), BlobPathThumbnail = Guid.NewGuid().ToString(),
                ContentType = "image/jpeg", UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1,
                UploaderId = "u1", Part = part
            });
            db.SaveChanges();
        }

        [Fact]
        public async Task OnGetAsync_WithNeedsTaggingOnlyFalse_ReturnsAllVehicles()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var withUntagged = SeedVehicle(db, "VIN1", DateTime.UtcNow.AddMinutes(-1));
            var withoutUntagged = SeedVehicle(db, "VIN2", DateTime.UtcNow);
            AddPhoto(db, withUntagged.Id, null);
            AddPhoto(db, withoutUntagged.Id, Part.FrontBumper);

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(2, model.Vehicles.Count);
        }

        [Fact]
        public async Task OnGetAsync_WithNeedsTaggingOnlyTrue_ExcludesVehiclesWithNoUntaggedPhotos()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var withUntagged = SeedVehicle(db, "VIN1", DateTime.UtcNow.AddMinutes(-1));
            var withoutUntagged = SeedVehicle(db, "VIN2", DateTime.UtcNow);
            AddPhoto(db, withUntagged.Id, null);
            AddPhoto(db, withoutUntagged.Id, Part.FrontBumper);

            var model = new IndexModel(db) { NeedsTaggingOnly = true };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(withUntagged.Id, model.Vehicles[0].Id);
            Assert.Equal(1, model.TotalCount);
        }

        [Fact]
        public async Task OnGetAsync_WithNeedsTaggingOnlyTrue_ExcludesVehiclesWithNoPhotosAtAll()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedVehicle(db, "VIN1", DateTime.UtcNow);

            var model = new IndexModel(db) { NeedsTaggingOnly = true };
            await model.OnGetAsync();

            Assert.Empty(model.Vehicles);
            Assert.Equal(0, model.TotalCount);
        }

        [Fact]
        public async Task OnGetAsync_WithNeedsTaggingOnlyTrue_CombinesWithSearch()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var matchWithUntagged = SeedVehicle(db, "MATCHVIN1", DateTime.UtcNow.AddMinutes(-1));
            var matchWithoutUntagged = SeedVehicle(db, "MATCHVIN2", DateTime.UtcNow);
            var nonMatchWithUntagged = SeedVehicle(db, "OTHER1", DateTime.UtcNow.AddMinutes(-2));
            AddPhoto(db, matchWithUntagged.Id, null);
            AddPhoto(db, matchWithoutUntagged.Id, Part.FrontBumper);
            AddPhoto(db, nonMatchWithUntagged.Id, null);

            var model = new IndexModel(db) { Search = "MATCH", NeedsTaggingOnly = true };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(matchWithUntagged.Id, model.Vehicles[0].Id);
        }

        [Fact]
        public async Task OnGetAsync_WithNeedsTaggingOnlyTrue_AndResultsBeyondLastPage_ClampsToLastPage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var withUntagged = SeedVehicle(db, "VIN1", DateTime.UtcNow.AddMinutes(-1));
            var withoutUntagged = SeedVehicle(db, "VIN2", DateTime.UtcNow);
            AddPhoto(db, withUntagged.Id, null);
            AddPhoto(db, withoutUntagged.Id, Part.FrontBumper);

            var model = new IndexModel(db) { NeedsTaggingOnly = true, PageNumber = 5 };
            await model.OnGetAsync();

            Assert.Equal(1, model.TotalPages);
            Assert.Equal(1, model.PageNumber);
            Assert.Single(model.Vehicles);
        }
    }
}
