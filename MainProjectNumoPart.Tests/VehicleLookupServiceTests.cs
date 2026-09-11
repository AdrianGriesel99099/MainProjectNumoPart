using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehicleLookupServiceTests
    {
        [Fact]
        public async Task FindOrCreateAsync_CreatesNewVehicleWhenNotFound()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");
            await db.SaveChangesAsync();

            Assert.NotEqual(0, vehicle.Id);
            Assert.Equal("1HGBH41JXMN109186", vehicle.BlobFolderName);
        }

        [Fact]
        public async Task FindOrCreateAsync_FindsExistingByVin()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var first = await service.FindOrCreateAsync("1HGBH41JXMN109186", null);
            await db.SaveChangesAsync();

            var second = await service.FindOrCreateAsync("1HGBH41JXMN109186", null);
            await db.SaveChangesAsync();

            Assert.Equal(first.Id, second.Id);
        }

        // The duplicate-vehicle bug: SQLite's default collation is case-SENSITIVE, so with only a
        // Trim() these all created a SECOND Vehicle row for the same physical car — a second blob
        // folder and a second set of sequence counters. UK regs really are written both ways.
        [Theory]
        [InlineData("ab12cde")]
        [InlineData("AB12 CDE")]
        [InlineData("  Ab12 cDe  ")]
        public async Task FindOrCreateAsync_MatchesExistingVehicleDespiteCaseAndSpacing(string retypedReg)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var first = await service.FindOrCreateAsync(null, "AB12CDE");
            await db.SaveChangesAsync();

            var second = await service.FindOrCreateAsync(null, retypedReg);
            await db.SaveChangesAsync();

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(1, await db.Vehicles.CountAsync());
        }

        [Fact]
        public async Task FindOrCreateAsync_StoresIdentifiersInCanonicalForm()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync(" 1hgbh41jxmn109186 ", "ab12 cde");
            await db.SaveChangesAsync();

            Assert.Equal("1HGBH41JXMN109186", vehicle.Vin);
            Assert.Equal("AB12CDE", vehicle.Reg);
            // The blob folder derives from the VIN, so it inherits the canonical form too.
            Assert.Equal("1HGBH41JXMN109186", vehicle.BlobFolderName);
        }

        [Theory]
        [InlineData("ab12cde")]
        [InlineData("AB12 CDE")]
        public async Task FindBySearchTermAsync_IsCaseAndWhitespaceInsensitive(string searchTerm)
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var created = await service.FindOrCreateAsync(null, "AB12CDE");
            await db.SaveChangesAsync();

            var found = await service.FindBySearchTermAsync(searchTerm);

            Assert.NotNull(found);
            Assert.Equal(created.Id, found!.Id);
        }

        // A blank term normalizes to null. It must find nothing — NOT match the first vehicle that
        // happens to have a null Vin (which a naive `v.Vin == null` comparison would do).
        [Fact]
        public async Task FindBySearchTermAsync_ReturnsNullForBlankTerm()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync(null, "AB12CDE"); // Reg-only, so Vin is null
            await db.SaveChangesAsync();

            Assert.Null(await service.FindBySearchTermAsync("   "));
        }

        [Fact]
        public async Task FindOrCreateAsync_BackfillsRegWithoutChangingFolderName()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var created = await service.FindOrCreateAsync(null, "CA481329");
            await db.SaveChangesAsync();
            var originalFolder = created.BlobFolderName;

            // The VIN turns up later for the same car.
            var found = await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");
            await db.SaveChangesAsync();

            Assert.Equal(created.Id, found.Id);
            Assert.Equal("1HGBH41JXMN109186", found.Vin);
            Assert.Equal(originalFolder, found.BlobFolderName); // Folder name never changes.
        }

        [Fact]
        public async Task FindOrCreateAsync_ThrowsWhenVinAndRegBelongToDifferentVehicles()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync("VIN1", null);
            await db.SaveChangesAsync();
            await service.FindOrCreateAsync(null, "REG2");
            await db.SaveChangesAsync();

            // VIN1 belongs to one vehicle, REG2 belongs to a different one — must reject, not guess.
            await Assert.ThrowsAsync<VehicleIdentifierConflictException>(
                () => service.FindOrCreateAsync("VIN1", "REG2"));
        }

        [Fact]
        public async Task FindBySearchTermAsync_MatchesEitherIdentifier()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");
            await db.SaveChangesAsync();

            var byVin = await service.FindBySearchTermAsync("1HGBH41JXMN109186");
            var byReg = await service.FindBySearchTermAsync("CA481329");

            Assert.NotNull(byVin);
            Assert.NotNull(byReg);
            Assert.Equal(byVin!.Id, byReg!.Id);
        }

        [Fact]
        public async Task FindBySearchTermAsync_ReturnsNullWhenNotFound()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var result = await service.FindBySearchTermAsync("NOTHINGHERE");

            Assert.Null(result);
        }

        [Fact]
        public async Task SaveWithRetryAsync_CommitsANewlyResolvedVehicle()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync("1HGBH41JXMN109186", null);
            var saved = await service.SaveWithRetryAsync(vehicle, "1HGBH41JXMN109186", null);

            Assert.NotEqual(0, saved.Id);
            Assert.Equal(1, await db.Vehicles.CountAsync());
        }

        // The race Upload.cshtml.cs's photo sequence-number retry already guards against, one
        // step earlier: two concurrent uploads for the same brand-new VIN both see "nothing
        // exists yet" before either commits. Reproduced deterministically here by having a
        // second context -- standing in for the concurrent request -- commit the same VIN in
        // between this attempt's lookup and its own save.
        [Fact]
        public async Task SaveWithRetryAsync_RecoversWhenAConcurrentRequestCreatesTheSameVehicleFirst()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            // This attempt's lookup runs and finds nothing -- the VIN is genuinely unclaimed
            // at this point.
            var losing = await service.FindOrCreateAsync("1HGBH41JXMN109186", null);

            // A concurrent request for the SAME brand-new VIN wins the race and commits first.
            using var concurrent = TestDbContextFactory.CreateSecondaryContext(db);
            var winning = new Vehicle
            {
                Vin = "1HGBH41JXMN109186",
                BlobFolderName = "1HGBH41JXMN109186",
                CreatedAtUtc = DateTime.UtcNow
            };
            concurrent.Vehicles.Add(winning);
            await concurrent.SaveChangesAsync();

            // Committing `losing` now collides with the winner's row on the unique Vin index.
            // SaveWithRetryAsync must not let that DbUpdateException propagate -- it should
            // discard the losing insert and hand back the vehicle the winner actually created.
            var resolved = await service.SaveWithRetryAsync(losing, "1HGBH41JXMN109186", null);

            Assert.Equal(winning.Id, resolved.Id);
            Assert.Equal(1, await db.Vehicles.CountAsync());
        }

        // Same race, but for Reg instead of Vin, and with the losing attempt tracking BOTH an
        // unset Vin and a Reg -- exercising the same backfill path FindOrCreateAsync already
        // has for a car logged Reg-only having its VIN discovered later, just reached via the
        // retry's re-resolve instead of a normal second call.
        [Fact]
        public async Task SaveWithRetryAsync_RecoversWhenAConcurrentRequestCreatesTheSameRegFirst()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var losing = await service.FindOrCreateAsync("1HGBH41JXMN109186", "CA481329");

            using var concurrent = TestDbContextFactory.CreateSecondaryContext(db);
            var winning = new Vehicle
            {
                Reg = "CA481329",
                BlobFolderName = "CA481329",
                CreatedAtUtc = DateTime.UtcNow
            };
            concurrent.Vehicles.Add(winning);
            await concurrent.SaveChangesAsync();

            var resolved = await service.SaveWithRetryAsync(losing, "1HGBH41JXMN109186", "CA481329");

            Assert.Equal(winning.Id, resolved.Id);
            Assert.Equal("1HGBH41JXMN109186", resolved.Vin); // backfilled onto the winner's row
            Assert.Equal(1, await db.Vehicles.CountAsync());
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_MatchesMakeModelCaseInsensitively()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync("1HGBH41JXMN109186", "AB12CDE");
            vehicle.MakeModel = "Ford Focus";
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("ford focus");

            Assert.Single(results);
            Assert.Equal(vehicle.Id, results[0].Id);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_MatchesPartialMakeModel()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync("1HGBH41JXMN109186", "AB12CDE");
            vehicle.MakeModel = "Ford Focus";
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("focus");

            Assert.Single(results);
            Assert.Equal(vehicle.Id, results[0].Id);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_MatchesPartialVinOrReg()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var vehicle = await service.FindOrCreateAsync("1HGBH41JXMN109186", "AB12CDE");
            await db.SaveChangesAsync();

            var byVinFragment = await service.FindManyBySearchTermAsync("MN109");
            var byRegFragment = await service.FindManyBySearchTermAsync("ab12");

            Assert.Single(byVinFragment);
            Assert.Equal(vehicle.Id, byVinFragment[0].Id);
            Assert.Single(byRegFragment);
            Assert.Equal(vehicle.Id, byRegFragment[0].Id);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_ReturnsMultipleMatches()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            var first = await service.FindOrCreateAsync("1HGBH41JXMN109186", "AB12CDE");
            first.MakeModel = "Ford Focus";
            var second = await service.FindOrCreateAsync("2HGBH41JXMN209187", "CD34EFG");
            second.MakeModel = "Ford Fiesta";
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("ford");

            Assert.Equal(2, results.Count);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_ReturnsEmptyForBlankTerm()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync(null, "AB12CDE");
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("   ");

            Assert.Empty(results);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_ReturnsEmptyWhenNothingMatches()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync("1HGBH41JXMN109186", "AB12CDE");
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("NOTHINGHERE");

            Assert.Empty(results);
        }

        // Backs the search-as-you-type suggestions endpoint, which wants far fewer rows than the
        // full "pick the right one" list this method otherwise returns.
        [Fact]
        public async Task FindManyBySearchTermAsync_WithLimit_CapsResultsBelowTheDefault()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            for (var i = 0; i < 5; i++)
            {
                var vehicle = await service.FindOrCreateAsync($"1HGBH41JXMN10918{i}", null);
                vehicle.MakeModel = "Ford Focus";
            }
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("focus", limit: 3);

            Assert.Equal(3, results.Count);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_WithLimit_ReturnsEmptyForBlankTerm()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            await service.FindOrCreateAsync(null, "AB12CDE");
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("   ", limit: 8);

            Assert.Empty(results);
        }

        [Fact]
        public async Task FindManyBySearchTermAsync_WithoutLimit_StillDefaultsToTwentyFive()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var service = new VehicleLookupService(db);

            for (var i = 0; i < 30; i++)
            {
                var vehicle = await service.FindOrCreateAsync($"1HGBH41JXMN1091{i:D2}", null);
                vehicle.MakeModel = "Ford Focus";
            }
            await db.SaveChangesAsync();

            var results = await service.FindManyBySearchTermAsync("focus");

            Assert.Equal(25, results.Count);
        }
    }
}
