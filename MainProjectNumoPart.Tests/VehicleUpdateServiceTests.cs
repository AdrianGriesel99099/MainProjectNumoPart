using System;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleUpdateServiceTests
    {
        private static VehicleUpdateService Build(Data.AppDbContext db)
            => new(db, NullLogger<VehicleUpdateService>.Instance);

        private static Vehicle SeedVehicle(Data.AppDbContext db)
        {
            var v = new Vehicle { Vin = "VIN123", BlobFolderName = "VIN123", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        [Fact]
        public async Task AddAsync_CreatesAnUpdateWithAttribution()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            var result = await Build(db).AddAsync(vehicle.Id, "Replaced the brake pads", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.Success, result.Status);
            var saved = db.VehicleUpdates.Single();
            Assert.Equal("Replaced the brake pads", saved.Body);
            Assert.Equal("u1", saved.AuthorId);
            Assert.Equal("staff@w.local", saved.AuthorEmail);
        }

        [Fact]
        public async Task AddAsync_TrimsWhitespace()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            await Build(db).AddAsync(vehicle.Id, "  padded text  ", "u1", "staff@w.local");

            Assert.Equal("padded text", db.VehicleUpdates.Single().Body);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task AddAsync_RejectsEmptyBody(string? body)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);

            var result = await Build(db).AddAsync(vehicle.Id, body, "u1", "staff@w.local");

            Assert.Equal(NoteStatus.EmptyBody, result.Status);
            Assert.Empty(db.VehicleUpdates);
        }

        [Fact]
        public async Task AddAsync_RejectsBodyOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            var tooLong = new string('x', VehicleUpdateService.MaxBodyLength + 1);

            var result = await Build(db).AddAsync(vehicle.Id, tooLong, "u1", "staff@w.local");

            Assert.Equal(NoteStatus.TooLong, result.Status);
            Assert.Empty(db.VehicleUpdates);
        }

        [Fact]
        public async Task AddAsync_ReturnsNotFoundForUnknownVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).AddAsync(999, "text", "u1", "staff@w.local");

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task DeleteAsync_RemovesTheUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).DeleteAsync(id);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.VehicleUpdates);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).DeleteAsync(999);

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }

        // Deleting a user is a separate flow (UserAdminService) that never touches VehicleUpdates
        // rows at all — this pins that an update survives independently of its author's account.
        [Fact]
        public async Task UpdateSurvivesIndependentlyOfAuthorAccount()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "text", "deleted-user-id", "gone@w.local");

            var saved = db.VehicleUpdates.Single();
            Assert.Equal("gone@w.local", saved.AuthorEmail);
        }

        // The whole point of editing in place rather than delete-and-repost: fixing a typo
        // shouldn't change who wrote it or when, or move it to a different point in the log.
        [Fact]
        public async Task EditAsync_ChangesBodyButKeepsAttributionAndTimestamp()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "Replaced the brake pdas", "u1", "staff@w.local");
            var original = db.VehicleUpdates.Single();
            var originalCreatedAt = original.CreatedAtUtc;

            var result = await Build(db).EditAsync(original.Id, "Replaced the brake pads");

            Assert.Equal(NoteStatus.Success, result.Status);
            var saved = db.VehicleUpdates.Single();
            Assert.Equal("Replaced the brake pads", saved.Body);
            Assert.Equal("u1", saved.AuthorId);
            Assert.Equal("staff@w.local", saved.AuthorEmail);
            Assert.Equal(originalCreatedAt, saved.CreatedAtUtc);
        }

        [Fact]
        public async Task EditAsync_TrimsWhitespace()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            await Build(db).EditAsync(id, "  padded text  ");

            Assert.Equal("padded text", db.VehicleUpdates.Single().Body);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task EditAsync_RejectsEmptyBody(string? body)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).EditAsync(id, body);

            Assert.Equal(NoteStatus.EmptyBody, result.Status);
            Assert.Equal("original", db.VehicleUpdates.Single().Body);
        }

        [Fact]
        public async Task EditAsync_RejectsBodyOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;
            var tooLong = new string('x', VehicleUpdateService.MaxBodyLength + 1);

            var result = await Build(db).EditAsync(id, tooLong);

            Assert.Equal(NoteStatus.TooLong, result.Status);
            Assert.Equal("original", db.VehicleUpdates.Single().Body);
        }

        [Fact]
        public async Task EditAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).EditAsync(999, "text");

            Assert.Equal(NoteStatus.NotFound, result.Status);
        }
    }
}
