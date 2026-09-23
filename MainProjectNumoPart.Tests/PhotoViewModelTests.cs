using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Photos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    // Covers Previous/Next navigation on the single-photo page: staff currently have to return to
    // the vehicle grid to move between photos one at a time. Navigation stays within the photo's
    // own stage, in SequenceNumber order -- the same grouping and ordering the vehicle page's grid
    // already uses (see Pages/Vehicles/Details.cshtml.cs), so "next" always means what it looks
    // like it means on the grid.
    public class PhotoViewModelTests
    {
        private static Photo SeedPhoto(AppDbContext db, Vehicle vehicle, Stage stage, int sequence)
        {
            var photo = new Photo
            {
                VehicleId = vehicle.Id, Stage = stage, FileName = "f.jpg",
                BlobPathOriginal = "o" + stage + sequence, BlobPathThumbnail = "t" + stage + sequence,
                ContentType = "image/jpeg", UploadedAtUtc = DateTime.UtcNow, SequenceNumber = sequence,
                UploaderId = "u1"
            };
            db.Photos.Add(photo);
            db.SaveChanges();
            return photo;
        }

        private static (ViewModel Model, AppDbContext Db) BuildModel()
        {
            var provider = TestIdentityFactory.Build();
            var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            return (new ViewModel(db, userManager), db);
        }

        [Fact]
        public async Task OnGetAsync_PhotoInMiddleOfStage_HasBothPreviousAndNext()
        {
            var (model, db) = BuildModel();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            var first = SeedPhoto(db, vehicle, Stage.Checkin, 1);
            var middle = SeedPhoto(db, vehicle, Stage.Checkin, 2);
            var last = SeedPhoto(db, vehicle, Stage.Checkin, 3);

            await model.OnGetAsync(middle.Id);

            Assert.Equal(first.Id, model.PreviousPhotoId);
            Assert.Equal(last.Id, model.NextPhotoId);
            Assert.Equal(2, model.PositionInStage);
            Assert.Equal(3, model.StagePhotoCount);
        }

        [Fact]
        public async Task OnGetAsync_FirstPhotoInStage_HasNoPrevious()
        {
            var (model, db) = BuildModel();
            var vehicle = new Vehicle { Vin = "V2", BlobFolderName = "V2", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            var first = SeedPhoto(db, vehicle, Stage.Checkin, 1);
            var second = SeedPhoto(db, vehicle, Stage.Checkin, 2);

            await model.OnGetAsync(first.Id);

            Assert.Null(model.PreviousPhotoId);
            Assert.Equal(second.Id, model.NextPhotoId);
        }

        [Fact]
        public async Task OnGetAsync_LastPhotoInStage_HasNoNext()
        {
            var (model, db) = BuildModel();
            var vehicle = new Vehicle { Vin = "V3", BlobFolderName = "V3", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            var first = SeedPhoto(db, vehicle, Stage.Checkin, 1);
            var last = SeedPhoto(db, vehicle, Stage.Checkin, 2);

            await model.OnGetAsync(last.Id);

            Assert.Equal(first.Id, model.PreviousPhotoId);
            Assert.Null(model.NextPhotoId);
        }

        [Fact]
        public async Task OnGetAsync_OnlyPhotoInStage_HasNeitherPreviousNorNext()
        {
            var (model, db) = BuildModel();
            var vehicle = new Vehicle { Vin = "V4", BlobFolderName = "V4", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            var only = SeedPhoto(db, vehicle, Stage.Checkin, 1);

            await model.OnGetAsync(only.Id);

            Assert.Null(model.PreviousPhotoId);
            Assert.Null(model.NextPhotoId);
            Assert.Equal(1, model.PositionInStage);
            Assert.Equal(1, model.StagePhotoCount);
        }

        // Navigation must not cross stage boundaries -- a "Next" from the last Check-in photo
        // should not silently jump into Quote photos the staff member wasn't browsing.
        [Fact]
        public async Task OnGetAsync_OtherStageOnSameVehicle_DoesNotAffectNavigation()
        {
            var (model, db) = BuildModel();
            var vehicle = new Vehicle { Vin = "V5", BlobFolderName = "V5", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            var checkin = SeedPhoto(db, vehicle, Stage.Checkin, 1);
            SeedPhoto(db, vehicle, Stage.Quote, 1);

            await model.OnGetAsync(checkin.Id);

            Assert.Null(model.PreviousPhotoId);
            Assert.Null(model.NextPhotoId);
            Assert.Equal(1, model.StagePhotoCount);
        }

        // Same stage, different vehicle -- must not be treated as the same navigation sequence.
        [Fact]
        public async Task OnGetAsync_SameStageOnDifferentVehicle_DoesNotAffectNavigation()
        {
            var (model, db) = BuildModel();
            var vehicleA = new Vehicle { Vin = "V6A", BlobFolderName = "V6A", CreatedAtUtc = DateTime.UtcNow };
            var vehicleB = new Vehicle { Vin = "V6B", BlobFolderName = "V6B", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.AddRange(vehicleA, vehicleB);
            db.SaveChanges();
            var photoA = SeedPhoto(db, vehicleA, Stage.Checkin, 1);
            SeedPhoto(db, vehicleB, Stage.Checkin, 1);

            await model.OnGetAsync(photoA.Id);

            Assert.Null(model.PreviousPhotoId);
            Assert.Null(model.NextPhotoId);
        }
    }
}
