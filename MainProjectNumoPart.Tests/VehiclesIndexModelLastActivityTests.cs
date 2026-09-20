using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers the "All vehicles" browse page's "Last activity" column -- lets staff spot which
    // jobs have gone quiet without opening each vehicle's Details page.
    public class VehiclesIndexModelLastActivityTests
    {
        [Fact]
        public async Task OnGetAsync_PopulatesLastActivityFromMostRecentJobCardUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow.AddDays(-10) };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow.AddDays(-5), SequenceNumber = 1, UploaderId = "u1"
            });
            var latest = DateTime.UtcNow.AddDays(-1);
            db.VehicleUpdates.Add(new VehicleUpdate
            {
                VehicleId = vehicle.Id, AuthorId = "u1", AuthorEmail = "u1@example.com",
                Body = "note", CreatedAtUtc = latest
            });
            db.SaveChanges();

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(latest, model.LastActivity[vehicle.Id]);
        }

        [Fact]
        public async Task OnGetAsync_VehicleWithNoActivityFallsBackToCreatedAtUtc()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var createdAt = DateTime.UtcNow.AddDays(-3);
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = createdAt };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(createdAt, model.LastActivity[vehicle.Id]);
        }

        [Fact]
        public async Task OnGetAsync_OnlyPopulatesLastActivityForVehiclesOnCurrentPage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
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

            var model = new IndexModel(db) { PageNumber = 1 };
            await model.OnGetAsync();

            Assert.DoesNotContain(model.Vehicles, v => v.Id == oldest.Id);
            Assert.False(model.LastActivity.ContainsKey(oldest.Id));
        }
    }
}
