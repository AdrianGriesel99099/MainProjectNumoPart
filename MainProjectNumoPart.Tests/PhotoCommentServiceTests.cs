using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoCommentServiceTests
    {
        private static PhotoCommentService Build(Data.AppDbContext db)
            => new(db, NullLogger<PhotoCommentService>.Instance);

        private static Photo SeedPhoto(Data.AppDbContext db)
        {
            var v = new Vehicle { Vin = "VIN123", BlobFolderName = "VIN123", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();

            var p = new Photo
            {
                VehicleId = v.Id, Stage = Stage.Checkin, FileName = "f.jpg",
                BlobPathOriginal = "o", BlobPathThumbnail = "t", ContentType = "image/jpeg",
                UploadedAtUtc = DateTime.UtcNow, SequenceNumber = 1, UploaderId = "u1"
            };
            db.Photos.Add(p);
            db.SaveChanges();
            return p;
        }

        [Fact]
        public async Task AddAsync_CreatesACommentWithAttribution()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);

            var result = await Build(db).AddAsync(photo.Id, "Scratch visible on the bumper", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.Success, result.Status);
            var saved = db.PhotoComments.Single();
            Assert.Equal("Scratch visible on the bumper", saved.Body);
            Assert.Equal("staff@w.local", saved.AuthorEmail);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task AddAsync_RejectsEmptyBody(string? body)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);

            var result = await Build(db).AddAsync(photo.Id, body, "u1", "staff@w.local");

            Assert.Equal(NoteStatus.EmptyBody, result.Status);
            Assert.Empty(db.PhotoComments);
        }

        [Fact]
        public async Task AddAsync_RejectsBodyOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            var tooLong = new string('x', PhotoCommentService.MaxBodyLength + 1);

            var result = await Build(db).AddAsync(photo.Id, tooLong, "u1", "staff@w.local");

            Assert.Equal(NoteStatus.TooLong, result.Status);
            Assert.Empty(db.PhotoComments);
        }

        [Fact]
        public async Task AddAsync_ReturnsNotFoundForUnknownPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).AddAsync(999, "text", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task DeleteAsync_RemovesTheComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).DeleteAsync(id);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.PhotoComments);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).DeleteAsync(999);

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task CommentSurvivesIndependentlyOfAuthorAccount()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "text", "deleted-user-id", "gone@w.local");

            Assert.Equal("gone@w.local", db.PhotoComments.Single().AuthorEmail);
        }
    }
}
