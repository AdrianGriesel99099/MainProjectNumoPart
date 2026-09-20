using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Pages.Vehicles;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleDetailsModelTests
    {
        private static Vehicle SeedVehicle(Data.AppDbContext db, string vin)
        {
            var vehicle = new Vehicle { Vin = vin, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            return vehicle;
        }

        private static Photo SeedPhoto(Data.AppDbContext db, int vehicleId, int sequenceNumber)
        {
            var photo = new Photo
            {
                VehicleId = vehicleId,
                Stage = Stage.Checkin,
                FileName = $"f{sequenceNumber}.jpg",
                BlobPathOriginal = $"o{sequenceNumber}",
                BlobPathThumbnail = $"t{sequenceNumber}",
                ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow,
                SequenceNumber = sequenceNumber,
                UploaderId = "u1"
            };
            db.Photos.Add(photo);
            db.SaveChanges();
            return photo;
        }

        [Fact]
        public async Task OnGetAsync_PopulatesCommentCounts_OnlyForPhotosWithComments()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "VIN1");
            var commented = SeedPhoto(db, vehicle.Id, 1);
            var uncommented = SeedPhoto(db, vehicle.Id, 2);

            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = commented.Id,
                AuthorId = "u1",
                AuthorEmail = "a@example.com",
                Body = "Looks good",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var model = new DetailsModel(db);
            await model.OnGetAsync(vehicle.Id);

            Assert.True(model.PhotoCommentCounts.ContainsKey(commented.Id));
            Assert.Equal(1, model.PhotoCommentCounts[commented.Id]);
            Assert.False(model.PhotoCommentCounts.ContainsKey(uncommented.Id));
        }

        [Fact]
        public async Task OnGetAsync_CountsMultipleCommentsOnSamePhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "VIN2");
            var photo = SeedPhoto(db, vehicle.Id, 1);

            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photo.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "First", CreatedAtUtc = DateTime.UtcNow
            });
            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photo.Id, AuthorId = "u2", AuthorEmail = "b@example.com",
                Body = "Second", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var model = new DetailsModel(db);
            await model.OnGetAsync(vehicle.Id);

            Assert.Equal(2, model.PhotoCommentCounts[photo.Id]);
        }

        [Fact]
        public async Task OnGetAsync_WithNoComments_ReturnsEmptyCommentCounts()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "VIN3");
            SeedPhoto(db, vehicle.Id, 1);

            var model = new DetailsModel(db);
            await model.OnGetAsync(vehicle.Id);

            Assert.Empty(model.PhotoCommentCounts);
        }

        [Fact]
        public async Task OnGetAsync_IgnoresCommentsOnOtherVehiclesPhotos()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db, "VIN4");
            var otherVehicle = SeedVehicle(db, "VIN5");
            var otherPhoto = SeedPhoto(db, otherVehicle.Id, 1);

            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = otherPhoto.Id, AuthorId = "u1", AuthorEmail = "a@example.com",
                Body = "Not this vehicle", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var model = new DetailsModel(db);
            await model.OnGetAsync(vehicle.Id);

            Assert.Empty(model.PhotoCommentCounts);
        }
    }
}
