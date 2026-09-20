using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers the home page's "Stage" column specifically -- Pages/Index.cshtml.cs wiring of
    // VehicleCurrentStage alongside the pre-existing UntaggedCounts dictionary.
    public class HomeIndexModelStageTests
    {
        [Fact]
        public async Task OnGetAsync_PopulatesCurrentStageForRecentVehicles()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkout, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1"
            });
            db.SaveChanges();

            var model = new Pages.IndexModel(db, new VehicleLookupService(db));
            await model.OnGetAsync();

            Assert.Equal(Stage.Checkout, model.CurrentStage[vehicle.Id]);
        }

        [Fact]
        public async Task OnGetAsync_VehicleWithNoPhotosHasNoCurrentStageEntry()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();

            var model = new Pages.IndexModel(db, new VehicleLookupService(db));
            await model.OnGetAsync();

            Assert.False(model.CurrentStage.ContainsKey(vehicle.Id));
        }
    }
}
