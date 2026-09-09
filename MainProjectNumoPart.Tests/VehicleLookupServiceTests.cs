using System.Threading.Tasks;
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
    }
}
