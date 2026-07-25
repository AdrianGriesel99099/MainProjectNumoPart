using System.Threading.Tasks;
using MainProjectNumoPart.Services;
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
    }
}
