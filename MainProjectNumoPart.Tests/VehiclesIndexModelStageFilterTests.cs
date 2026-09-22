using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers the "All vehicles" browse page's stage filter -- narrows the list to vehicles whose
    // current stage (per VehicleCurrentStage, the Stage column already shown on this page) matches
    // the selected value, so staff can pull up e.g. every vehicle sitting at Checkout without
    // scanning the Stage column page by page.
    public class VehiclesIndexModelStageFilterTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin)
        {
            var vehicle = new Vehicle
            {
                Vin = vin,
                BlobFolderName = vin,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            return vehicle;
        }

        private static void AddPhoto(Data.AppDbContext db, int vehicleId, Stage stage, DateTime uploadedAtUtc)
        {
            db.Photos.Add(new Photo
            {
                VehicleId = vehicleId, Stage = stage, FileName = "f.jpg",
                BlobPathOriginal = Guid.NewGuid().ToString(), BlobPathThumbnail = Guid.NewGuid().ToString(),
                ContentType = "image/jpeg", UploadedAtUtc = uploadedAtUtc, SequenceNumber = 1,
                UploaderId = "u1"
            });
            db.SaveChanges();
        }

        [Fact]
        public async Task OnGetAsync_WithNoStageFilter_ReturnsAllVehicles()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v1 = SeedVehicle(db, "VIN1");
            AddPhoto(db, v1.Id, Stage.Checkin, DateTime.UtcNow);
            var v2 = SeedVehicle(db, "VIN2");
            AddPhoto(db, v2.Id, Stage.Checkout, DateTime.UtcNow);

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(2, model.Vehicles.Count);
        }

        [Fact]
        public async Task OnGetAsync_WithStageFilter_ReturnsOnlyVehiclesAtThatStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var checkout = SeedVehicle(db, "VIN1");
            AddPhoto(db, checkout.Id, Stage.Checkout, DateTime.UtcNow);
            var quote = SeedVehicle(db, "VIN2");
            AddPhoto(db, quote.Id, Stage.Quote, DateTime.UtcNow);

            var model = new IndexModel(db) { StageFilter = Stage.Checkout };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(checkout.Id, model.Vehicles[0].Id);
            Assert.Equal(1, model.TotalCount);
        }

        [Fact]
        public async Task OnGetAsync_WithStageFilter_UsesMostRecentPhotoWhenVehicleHasMultipleStages()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "VIN1");
            AddPhoto(db, vehicle.Id, Stage.Checkin, DateTime.UtcNow.AddDays(-2));
            AddPhoto(db, vehicle.Id, Stage.Checkout, DateTime.UtcNow);

            var checkoutModel = new IndexModel(db) { StageFilter = Stage.Checkout };
            await checkoutModel.OnGetAsync();
            Assert.Single(checkoutModel.Vehicles);

            var checkinModel = new IndexModel(db) { StageFilter = Stage.Checkin };
            await checkinModel.OnGetAsync();
            Assert.Empty(checkinModel.Vehicles);
        }

        [Fact]
        public async Task OnGetAsync_WithStageFilter_ExcludesVehiclesWithNoPhotos()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedVehicle(db, "VIN1");

            var model = new IndexModel(db) { StageFilter = Stage.Checkin };
            await model.OnGetAsync();

            Assert.Empty(model.Vehicles);
        }

        [Fact]
        public async Task OnGetAsync_WithStageFilter_CombinesWithSearch()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var matchAtCheckout = SeedVehicle(db, "MATCHVIN1");
            AddPhoto(db, matchAtCheckout.Id, Stage.Checkout, DateTime.UtcNow);
            var matchAtQuote = SeedVehicle(db, "MATCHVIN2");
            AddPhoto(db, matchAtQuote.Id, Stage.Quote, DateTime.UtcNow);
            var nonMatchAtCheckout = SeedVehicle(db, "OTHER1");
            AddPhoto(db, nonMatchAtCheckout.Id, Stage.Checkout, DateTime.UtcNow);

            var model = new IndexModel(db) { Search = "MATCH", StageFilter = Stage.Checkout };
            await model.OnGetAsync();

            Assert.Single(model.Vehicles);
            Assert.Equal(matchAtCheckout.Id, model.Vehicles[0].Id);
        }

        [Fact]
        public async Task OnGetAsync_WithStageFilter_AndResultsBeyondLastPage_ClampsToLastPage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var checkout = SeedVehicle(db, "VIN1");
            AddPhoto(db, checkout.Id, Stage.Checkout, DateTime.UtcNow);
            var quote = SeedVehicle(db, "VIN2");
            AddPhoto(db, quote.Id, Stage.Quote, DateTime.UtcNow);

            var model = new IndexModel(db) { StageFilter = Stage.Checkout, PageNumber = 5 };
            await model.OnGetAsync();

            Assert.Equal(1, model.TotalPages);
            Assert.Equal(1, model.PageNumber);
            Assert.Single(model.Vehicles);
        }
    }
}
