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
        public async Task DeleteAsync_AllowsTheAuthorToRemoveTheirOwnUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).DeleteAsync(id, "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.VehicleUpdates);
        }

        [Fact]
        public async Task DeleteAsync_AllowsAnAdminToRemoveSomeoneElsesUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).DeleteAsync(id, "admin1", requesterIsAdmin: true);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Empty(db.VehicleUpdates);
        }

        [Fact]
        public async Task DeleteAsync_RejectsANonAdminDeletingSomeoneElsesUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).DeleteAsync(id, "u2", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Forbidden, result.Status);
            Assert.Single(db.VehicleUpdates);
        }

        [Fact]
        public async Task DeleteAsync_ReturnsNotFoundForUnknownId()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).DeleteAsync(999, "u1", requesterIsAdmin: false);

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

        [Fact]
        public async Task EditAsync_AllowsTheAuthorToChangeTheirOwnUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).EditAsync(id, "corrected text", "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Equal("corrected text", db.VehicleUpdates.Single().Body);
        }

        [Fact]
        public async Task EditAsync_TrimsWhitespace()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            await Build(db).EditAsync(id, "  padded edit  ", "u1", requesterIsAdmin: false);

            Assert.Equal("padded edit", db.VehicleUpdates.Single().Body);
        }

        [Fact]
        public async Task EditAsync_DoesNotChangeCreatedAtUtc()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var original = db.VehicleUpdates.Single();
            var createdAt = original.CreatedAtUtc;

            await Build(db).EditAsync(original.Id, "corrected text", "u1", requesterIsAdmin: false);

            Assert.Equal(createdAt, db.VehicleUpdates.Single().CreatedAtUtc);
        }

        [Fact]
        public async Task EditAsync_AllowsAnAdminToChangeSomeoneElsesUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).EditAsync(id, "corrected by admin", "admin1", requesterIsAdmin: true);

            Assert.Equal(NoteStatus.Success, result.Status);
            Assert.Equal("corrected by admin", db.VehicleUpdates.Single().Body);
        }

        [Fact]
        public async Task EditAsync_RejectsANonAdminEditingSomeoneElsesUpdate()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).EditAsync(id, "hijacked text", "u2", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.Forbidden, result.Status);
            Assert.Equal("original text", db.VehicleUpdates.Single().Body);
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
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;

            var result = await Build(db).EditAsync(id, body, "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.EmptyBody, result.Status);
            Assert.Equal("original text", db.VehicleUpdates.Single().Body);
        }

        [Fact]
        public async Task EditAsync_RejectsBodyOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = SeedVehicle(db);
            await Build(db).AddAsync(vehicle.Id, "original text", "u1", "staff@w.local");
            var id = db.VehicleUpdates.Single().Id;
            var tooLong = new string('x', VehicleUpdateService.MaxBodyLength + 1);

            var result = await Build(db).EditAsync(id, tooLong, "u1", requesterIsAdmin: false);

            Assert.Equal(NoteStatus.TooLong, result.Status);
            Assert.Equal("original text", db.VehicleUpdates.Single().Body);
        }
    }
}
