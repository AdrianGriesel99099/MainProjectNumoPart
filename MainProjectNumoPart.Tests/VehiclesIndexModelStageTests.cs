using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers the "All vehicles" browse page's "Stage" column -- the home page's vehicle lists
    // already show current stage via VehicleCurrentStage (see HomeIndexModelStageTests), but this
    // page was missing it, leaving no way to see a vehicle's stage while paging/searching here.
    public class VehiclesIndexModelStageTests
    {
        [Fact]
        public async Task OnGetAsync_PopulatesCurrentStageForListedVehicles()
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

            var model = new IndexModel(db);
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

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.False(model.CurrentStage.ContainsKey(vehicle.Id));
        }

        [Fact]
        public async Task OnGetAsync_OnlyPopulatesStageForVehiclesOnCurrentPage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            // Oldest, so it lands on page 2 once there are more than PageSize vehicles.
            var oldest = new Vehicle { Vin = "OLDEST", BlobFolderName = "OLDEST", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) };
            db.Vehicles.Add(oldest);
            for (var i = 1; i <= IndexModel.PageSize; i++)
            {
                db.Vehicles.Add(new Vehicle
                {
                    Vin = $"VIN{i}", BlobFolderName = $"VIN{i}",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
                });
            }
            db.SaveChanges();
            db.Photos.Add(new Photo
            {
                VehicleId = oldest.Id, Stage = Stage.Progress, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1"
            });
            db.SaveChanges();

            // "oldest" is pushed to page 2 -- its stage should not be computed/exposed on page 1.
            var model = new IndexModel(db) { PageNumber = 1 };
            await model.OnGetAsync();

            Assert.DoesNotContain(model.Vehicles, v => v.Id == oldest.Id);
            Assert.False(model.CurrentStage.ContainsKey(oldest.Id));
        }
    }
}
