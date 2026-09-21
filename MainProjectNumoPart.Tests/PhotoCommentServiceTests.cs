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
        public async Task DeleteAsync_AllowsTheAuthorToRemoveTheirOwnComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).DeleteAsync(id, "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.PhotoComments);
        }

        [Fact]
        public async Task DeleteAsync_AllowsAnAdminToRemoveSomeoneElsesComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).DeleteAsync(id, "admin1", requesterIsAdmin: true);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.PhotoComments);
        }

        [Fact]
        public async Task DeleteAsync_RejectsANonAdminDeletingSomeoneElsesComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).DeleteAsync(id, "u2", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Forbidden, result.Status);
            Assert.Single(db.PhotoComments);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).DeleteAsync(999, "u1", requesterIsAdmin: false);

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

        [Fact]
        public async Task EditAsync_AllowsTheAuthorToChangeTheirOwnComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).EditAsync(id, "corrected text", "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Equal("corrected text", db.PhotoComments.Single().Body);
        }

        [Fact]
        public async Task EditAsync_TrimsWhitespace()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            await Build(db).EditAsync(id, "  padded edit  ", "u1", requesterIsAdmin: false);

            Assert.Equal("padded edit", db.PhotoComments.Single().Body);
        }

        [Fact]
        public async Task EditAsync_DoesNotChangeCreatedAtUtc()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var original = db.PhotoComments.Single();
            var createdAt = original.CreatedAtUtc;

            await Build(db).EditAsync(original.Id, "corrected text", "u1", requesterIsAdmin: false);

            Assert.Equal(createdAt, db.PhotoComments.Single().CreatedAtUtc);
        }

        [Fact]
        public async Task EditAsync_AllowsAnAdminToChangeSomeoneElsesComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).EditAsync(id, "corrected by admin", "admin1", requesterIsAdmin: true);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Equal("corrected by admin", db.PhotoComments.Single().Body);
        }

        [Fact]
        public async Task EditAsync_RejectsANonAdminEditingSomeoneElsesComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).EditAsync(id, "hijacked text", "u2", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Forbidden, result.Status);
            Assert.Equal("original text", db.PhotoComments.Single().Body);
        }

        [Fact]
        public async Task EditAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).EditAsync(999, "text", "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task EditAsync_RejectsEmptyBody(string? body)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;

            var result = await Build(db).EditAsync(id, body, "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.EmptyBody, result.Status);
            Assert.Equal("original text", db.PhotoComments.Single().Body);
        }

        [Fact]
        public async Task EditAsync_RejectsBodyOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var photo = SeedPhoto(db);
            await Build(db).AddAsync(photo.Id, "original text", "u1", "staff@w.local");
            var id = db.PhotoComments.Single().Id;
            var tooLong = new string('x', PhotoCommentService.MaxBodyLength + 1);

            var result = await Build(db).EditAsync(id, tooLong, "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.TooLong, result.Status);
            Assert.Equal("original text", db.PhotoComments.Single().Body);
        }
    }
}
