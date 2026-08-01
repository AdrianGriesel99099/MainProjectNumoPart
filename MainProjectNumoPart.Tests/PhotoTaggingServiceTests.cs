using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoTaggingServiceTests
    {
        private static PhotoTaggingService Build(AppDbContext db)
            => new(db, NullLogger<PhotoTaggingService>.Instance);

        private static Vehicle SeedVehicle(AppDbContext db, string vin = "VIN123")
        {
            var v = new Vehicle { Vin = vin, BlobFolderName = vin, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static List<Photo> SeedPhotos(AppDbContext db, Vehicle vehicle, int count, Part? part = null)
        {
            var photos = new List<Photo>();
            for (var i = 1; i <= count; i++)
            {
                var p = new Photo
                {
                    VehicleId = vehicle.Id,
                    Stage = Stage.Checkin,
                    Part = part,
                    FileName = $"p{i}.jpg",
                    BlobPathOriginal = $"{vehicle.BlobFolderName}/Checkin/p{i}.jpg",
                    BlobPathThumbnail = $"{vehicle.BlobFolderName}/Checkin/p{i}-t.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = 10,
                    UploadedAtUtc = DateTime.UtcNow,
                    SequenceNumber = i,
                    UploaderId = "u1"
                };
                db.Photos.Add(p);
                photos.Add(p);
            }
            db.SaveChanges();
            return photos;
        }

        [Fact]
        public async Task SetsPartOnEverySelectedPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 3);

            var result = await Build(db).SetPartAsync(photos.Select(p => p.Id).ToList(), Part.FrontBumper, "u1");

            Assert.Equal(PhotoTagStatus.Updated, result.Status);
            Assert.Equal(3, result.Updated);
            Assert.All(db.Photos.ToList(), p => Assert.Equal(Part.FrontBumper, p.Part));
        }

        // Clearing is how a mis-tag gets undone. Without it the only remedy would be deleting
        // and re-uploading the photo.
        [Fact]
        public async Task NullPartClearsAnExistingTag()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 2, Part.Bonnet);

            var result = await Build(db).SetPartAsync(photos.Select(p => p.Id).ToList(), null, "u1");

            Assert.Equal(PhotoTagStatus.Updated, result.Status);
            Assert.All(db.Photos.ToList(), p => Assert.Null(p.Part));
        }

        [Fact]
        public async Task LeavesUnselectedPhotosAlone()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 4);

            await Build(db).SetPartAsync(new[] { photos[0].Id, photos[1].Id }, Part.Roof, "u1");

            Assert.Equal(Part.Roof, db.Photos.Single(p => p.Id == photos[0].Id).Part);
            Assert.Equal(Part.Roof, db.Photos.Single(p => p.Id == photos[1].Id).Part);
            Assert.Null(db.Photos.Single(p => p.Id == photos[2].Id).Part);
            Assert.Null(db.Photos.Single(p => p.Id == photos[3].Id).Part);
        }

        // A photo can be deleted between the page rendering and the tag being applied. Failing
        // the whole batch over one stale id would be worse than tagging the rest.
        [Fact]
        public async Task IgnoresUnknownIdsAndTagsTheRest()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 2);

            var result = await Build(db).SetPartAsync(new[] { photos[0].Id, 999999 }, Part.Windscreen, "u1");

            Assert.Equal(PhotoTagStatus.Updated, result.Status);
            Assert.Equal(1, result.Updated);
            Assert.Equal(Part.Windscreen, db.Photos.Single(p => p.Id == photos[0].Id).Part);
        }

        [Fact]
        public async Task DuplicateIdsAreCountedOnce()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 1);

            var id = photos[0].Id;
            var result = await Build(db).SetPartAsync(new[] { id, id, id }, Part.Bonnet, "u1");

            Assert.Equal(1, result.Updated);
        }

        [Theory]
        [InlineData(0)]
        public async Task RejectsEmptySelection(int count)
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).SetPartAsync(new int[count], Part.Bonnet, "u1");

            Assert.Equal(PhotoTagStatus.NoPhotosSelected, result.Status);
        }

        [Fact]
        public async Task RejectsMoreThanTheCapAndChangesNothing()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 2);

            var tooMany = Enumerable.Range(1, PhotoTaggingService.MaxPhotosPerRequest + 1).ToArray();
            var result = await Build(db).SetPartAsync(tooMany, Part.Bonnet, "u1");

            Assert.Equal(PhotoTagStatus.TooManySelected, result.Status);
            Assert.All(db.Photos.ToList(), p => Assert.Null(p.Part));
        }

        [Fact]
        public async Task RetaggingOverwritesThePreviousPart()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 1, Part.Bonnet);

            await Build(db).SetPartAsync(new[] { photos[0].Id }, Part.Roof, "u1");

            Assert.Equal(Part.Roof, db.Photos.Single().Part);
        }

        // Tagging is metadata only — it must never touch the blob paths, or a re-tag would
        // orphan the stored image.
        [Fact]
        public async Task DoesNotAlterBlobPaths()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var v = SeedVehicle(db);
            var photos = SeedPhotos(db, v, 1);
            var originalPath = photos[0].BlobPathOriginal;
            var thumbPath = photos[0].BlobPathThumbnail;

            await Build(db).SetPartAsync(new[] { photos[0].Id }, Part.QuarterRearLeft, "u1");

            var saved = db.Photos.Single();
            Assert.Equal(originalPath, saved.BlobPathOriginal);
            Assert.Equal(thumbPath, saved.BlobPathThumbnail);
        }
    }
}
