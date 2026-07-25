using System;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoSequenceAllocatorTests
    {
        [Fact]
        public async Task NextSequenceNumberAsync_StartsAtOne()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();

            var allocator = new PhotoSequenceAllocator(db);
            var next = await allocator.NextSequenceNumberAsync(vehicle.Id, Stage.Checkin);

            Assert.Equal(1, next);
        }

        [Fact]
        public async Task NextSequenceNumberAsync_IncrementsPerVehicleAndStage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var vehicle = new Vehicle { Vin = "V1", BlobFolderName = "V1", CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();

            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id, Stage = Stage.Checkin, SequenceNumber = 1,
                FileName = "a.jpg", BlobPathOriginal = "a", BlobPathThumbnail = "a",
                ContentType = "image/jpeg", UploadedAtUtc = DateTime.UtcNow, UploaderId = "u1"
            });
            await db.SaveChangesAsync();

            var allocator = new PhotoSequenceAllocator(db);

            Assert.Equal(2, await allocator.NextSequenceNumberAsync(vehicle.Id, Stage.Checkin));
            Assert.Equal(1, await allocator.NextSequenceNumberAsync(vehicle.Id, Stage.Quote)); // different stage, own counter
        }
    }
}
