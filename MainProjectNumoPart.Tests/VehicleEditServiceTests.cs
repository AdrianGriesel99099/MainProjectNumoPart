using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleEditServiceTests
    {
        private static VehicleEditService Build(AppDbContext db)
            => new(db, NullLogger<VehicleEditService>.Instance);

        private static Vehicle Seed(AppDbContext db, string? vin, string? reg, string? makeModel = null)
        {
            var vehicle = new Vehicle
            {
                Vin = vin,
                Reg = reg,
                MakeModel = makeModel,
                BlobFolderName = vin ?? reg!,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();
            return vehicle;
        }

        [Fact]
        public async Task UpdatesAllThreeFields()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "OLDVIN1", "OLD111");

            var result = await Build(db).UpdateAsync(vehicle.Id, "NEWVIN1", "NEW222", "Toyota Hilux", "u1");

            Assert.Equal(VehicleEditStatus.Updated, result.Status);
            var saved = db.Vehicles.Single();
            Assert.Equal("NEWVIN1", saved.Vin);
            Assert.Equal("NEW222", saved.Reg);
            Assert.Equal("Toyota Hilux", saved.MakeModel);
        }

        // The whole point of the edit page for this field: MakeModel has no other way to be set.
        [Fact]
        public async Task SetsMakeModelOnAVehicleThatHadNone()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");
            Assert.Null(vehicle.MakeModel);

            await Build(db).UpdateAsync(vehicle.Id, "VIN123", "AB12CDE", "Ford Ranger", "u1");

            Assert.Equal("Ford Ranger", db.Vehicles.Single().MakeModel);
        }

        // BlobFolderName is the storage key every existing photo's path was built from. Changing it
        // would orphan every image the vehicle already has.
        [Fact]
        public async Task NeverChangesBlobFolderNameEvenWhenVinChanges()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "ORIGINALVIN", null);
            var originalFolder = vehicle.BlobFolderName;

            await Build(db).UpdateAsync(vehicle.Id, "COMPLETELYNEWVIN", null, null, "u1");

            Assert.Equal(originalFolder, db.Vehicles.Single().BlobFolderName);
        }

        [Theory]
        [InlineData("ab12 cde", "AB12CDE")]
        [InlineData("  vin 123  ", "VIN123")]
        public async Task NormalizesIdentifiersOnSave(string typed, string expected)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "SEEDVIN", null);

            await Build(db).UpdateAsync(vehicle.Id, typed, null, null, "u1");

            Assert.Equal(expected, db.Vehicles.Single().Vin);
        }

        [Fact]
        public async Task RejectsVinAlreadyUsedByAnotherVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var target = Seed(db, "MYVIN", "MYREG");
            Seed(db, "TAKENVIN", "OTHERREG");

            var result = await Build(db).UpdateAsync(target.Id, "TAKENVIN", "MYREG", null, "u1");

            Assert.Equal(VehicleEditStatus.VinConflict, result.Status);
            Assert.Equal("MYVIN", db.Vehicles.Single(v => v.Id == target.Id).Vin);
        }

        [Fact]
        public async Task RejectsRegAlreadyUsedByAnotherVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var target = Seed(db, "MYVIN", "MYREG");
            Seed(db, "OTHERVIN", "TAKENREG");

            var result = await Build(db).UpdateAsync(target.Id, "MYVIN", "TAKENREG", null, "u1");

            Assert.Equal(VehicleEditStatus.RegConflict, result.Status);
            Assert.Equal("MYREG", db.Vehicles.Single(v => v.Id == target.Id).Reg);
        }

        // Re-saving an unchanged form must not collide with the vehicle's own identifiers.
        [Fact]
        public async Task AllowsSavingItsOwnUnchangedIdentifiers()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");

            var result = await Build(db).UpdateAsync(vehicle.Id, "VIN123", "AB12CDE", "Mazda", "u1");

            Assert.Equal(VehicleEditStatus.Updated, result.Status);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("", "")]
        [InlineData("   ", "  ")]
        public async Task RejectsClearingBothIdentifiers(string? vin, string? reg)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");

            var result = await Build(db).UpdateAsync(vehicle.Id, vin, reg, null, "u1");

            Assert.Equal(VehicleEditStatus.NoIdentifier, result.Status);
            var saved = db.Vehicles.Single();
            Assert.Equal("VIN123", saved.Vin);
            Assert.Equal("AB12CDE", saved.Reg);
        }

        [Fact]
        public async Task AllowsClearingOneIdentifierWhenTheOtherRemains()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", "AB12CDE");

            var result = await Build(db).UpdateAsync(vehicle.Id, "VIN123", null, null, "u1");

            Assert.Equal(VehicleEditStatus.Updated, result.Status);
            Assert.Null(db.Vehicles.Single().Reg);
        }

        [Fact]
        public async Task BlankMakeModelIsStoredAsNullNotEmptyString()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "VIN123", null, "Toyota");

            await Build(db).UpdateAsync(vehicle.Id, "VIN123", null, "   ", "u1");

            Assert.Null(db.Vehicles.Single().MakeModel);
        }

        // The VinConflict/RegConflict pre-checks are a separate round-trip from the SaveChangesAsync
        // that actually commits, so two concurrent edits that both pass the pre-check (neither sees
        // the other's not-yet-committed VIN) can still collide on the unique index. Reproduced
        // deterministically, the same way VehicleLookupServiceTests reproduces its own save race:
        // a SaveChangesInterceptor commits the "concurrent" edit at the exact moment between this
        // attempt's pre-check and its own save, guaranteeing the interleaving instead of hoping a
        // real thread schedules that way.
        [Fact]
        public async Task RecoversWhenAConcurrentEditClaimsTheSameVinFirst()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            using var db = new AppDbContext(options);
            db.Database.EnsureCreated();

            var target = Seed(db, "MYVIN", "MYREG");
            var other = Seed(db, "OTHERVIN", "OTHERREG");

            var interceptor = new ClaimVinOnFirstSaveInterceptor(connection, other.Id, "STOLENVIN");
            var raceOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptor)
                .Options;
            using var raceDb = new AppDbContext(raceOptions);

            var result = await new VehicleEditService(raceDb, NullLogger<VehicleEditService>.Instance)
                .UpdateAsync(target.Id, "STOLENVIN", "MYREG", null, "u1");

            Assert.Equal(VehicleEditStatus.VinConflict, result.Status);
            // Both records left alone: the loser keeps its original VIN, the winner keeps its
            // claim. AsNoTracking because `db` still has its own stale, tracked copy of `other`
            // from Seed() -- the winning commit happened through an entirely different context.
            Assert.Equal("MYVIN", await db.Vehicles.AsNoTracking().Where(v => v.Id == target.Id).Select(v => v.Vin).SingleAsync());
            Assert.Equal("STOLENVIN", await db.Vehicles.AsNoTracking().Where(v => v.Id == other.Id).Select(v => v.Vin).SingleAsync());
        }

        // Fires exactly once, right before the intercepted context's own SaveChangesAsync sends its
        // commands, standing in for a second request that wins the race to claim a VIN first.
        private sealed class ClaimVinOnFirstSaveInterceptor : SaveChangesInterceptor
        {
            private readonly SqliteConnection _connection;
            private readonly int _otherVehicleId;
            private readonly string _conflictingVin;
            private bool _fired;

            public ClaimVinOnFirstSaveInterceptor(SqliteConnection connection, int otherVehicleId, string conflictingVin)
            {
                _connection = connection;
                _otherVehicleId = otherVehicleId;
                _conflictingVin = conflictingVin;
            }

            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (!_fired)
                {
                    _fired = true;
                    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
                    using var concurrent = new AppDbContext(options);
                    var other = await concurrent.Vehicles.FirstAsync(v => v.Id == _otherVehicleId, cancellationToken);
                    other.Vin = _conflictingVin;
                    await concurrent.SaveChangesAsync(cancellationToken);
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        [Fact]
        public async Task ReturnsNotFoundForUnknownVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).UpdateAsync(999999, "VIN", null, null, "u1");

            Assert.Equal(VehicleEditStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task LeavesPhotosAttachedAfterVinChange()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = Seed(db, "OLDVIN", null);
            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id,
                Stage = Stage.Checkin,
                FileName = "p.jpg",
                BlobPathOriginal = "OLDVIN/Checkin/p.jpg",
                BlobPathThumbnail = "OLDVIN/Checkin/p-thumb.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 1,
                UploadedAtUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                UploaderId = "u1"
            });
            db.SaveChanges();

            await Build(db).UpdateAsync(vehicle.Id, "BRANDNEWVIN", null, null, "u1");

            var photo = db.Photos.Single();
            Assert.Equal(vehicle.Id, photo.VehicleId);
            // Existing blob paths are untouched — they still point at the original folder.
            Assert.Equal("OLDVIN/Checkin/p.jpg", photo.BlobPathOriginal);
        }
    }
}
