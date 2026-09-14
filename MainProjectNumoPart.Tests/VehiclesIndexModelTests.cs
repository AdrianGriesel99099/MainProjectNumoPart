using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehiclesIndexModelTests
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

        [Fact]
        public async Task OnGetAsync_WithFewerVehiclesThanPageSize_ReturnsAllOnOnePage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            for (var i = 1; i <= 3; i++)
            {
                SeedVehicle(db, $"VIN{i}", DateTime.UtcNow.AddMinutes(-i));
            }

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(3, model.Vehicles.Count);
            Assert.Equal(3, model.TotalCount);
            Assert.Equal(1, model.TotalPages);
        }

        [Fact]
        public async Task OnGetAsync_OrdersNewestFirst()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var older = SeedVehicle(db, "OLD", DateTime.UtcNow.AddDays(-2));
            var newer = SeedVehicle(db, "NEW", DateTime.UtcNow.AddDays(-1));

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.Equal(newer.Id, model.Vehicles[0].Id);
            Assert.Equal(older.Id, model.Vehicles[1].Id);
        }

        [Fact]
        public async Task OnGetAsync_WithMoreVehiclesThanPageSize_PaginatesAcrossPages()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            for (var i = 1; i <= 30; i++)
            {
                // Later i == more recently created, so VIN30 is newest.
                SeedVehicle(db, $"VIN{i}", DateTime.UtcNow.AddMinutes(-(30 - i)));
            }

            var firstPage = new IndexModel(db) { PageNumber = 1 };
            await firstPage.OnGetAsync();

            Assert.Equal(IndexModel.PageSize, firstPage.Vehicles.Count);
            Assert.Equal(30, firstPage.TotalCount);
            Assert.Equal(2, firstPage.TotalPages);
            Assert.Equal("VIN30", firstPage.Vehicles.First().Vin);

            var secondPage = new IndexModel(db) { PageNumber = 2 };
            await secondPage.OnGetAsync();

            Assert.Equal(30 - IndexModel.PageSize, secondPage.Vehicles.Count);
            Assert.Equal("VIN1", secondPage.Vehicles.Last().Vin);

            Assert.Empty(firstPage.Vehicles.Select(v => v.Id).Intersect(secondPage.Vehicles.Select(v => v.Id)));
        }

        [Fact]
        public async Task OnGetAsync_WithPageLessThanOne_ClampsToFirstPage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            SeedVehicle(db, "VIN1", DateTime.UtcNow);

            var model = new IndexModel(db) { PageNumber = 0 };
            await model.OnGetAsync();

            Assert.Equal(1, model.PageNumber);
            Assert.Single(model.Vehicles);
        }

        [Fact]
        public async Task OnGetAsync_PopulatesUntaggedCounts_OnlyForVehiclesWithUntaggedPhotos()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var withUntagged = SeedVehicle(db, "VIN1", DateTime.UtcNow.AddMinutes(-1));
            var withoutUntagged = SeedVehicle(db, "VIN2", DateTime.UtcNow);

            db.Photos.Add(new Photo
            {
                VehicleId = withUntagged.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o1", BlobPathThumbnail = "t1", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1", Part = null
            });
            db.Photos.Add(new Photo
            {
                VehicleId = withoutUntagged.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o2", BlobPathThumbnail = "t2", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1", Part = Part.FrontBumper
            });
            db.SaveChanges();

            var model = new IndexModel(db);
            await model.OnGetAsync();

            Assert.True(model.UntaggedCounts.ContainsKey(withUntagged.Id));
            Assert.False(model.UntaggedCounts.ContainsKey(withoutUntagged.Id));
        }
    }
}
