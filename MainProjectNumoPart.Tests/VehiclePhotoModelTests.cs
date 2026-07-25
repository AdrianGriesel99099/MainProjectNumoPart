using System;
using System.Linq;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class VehiclePhotoModelTests
    {
        [Fact]
        public void SavesAndReloadsVehicleWithPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var vehicle = new Vehicle
            {
                Vin = "1HGBH41JXMN109186",
                BlobFolderName = "1HGBH41JXMN109186",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Vehicles.Add(vehicle);
            db.SaveChanges();

            db.Photos.Add(new Photo
            {
                VehicleId = vehicle.Id,
                Stage = Stage.Checkin,
                FileName = "1HGBH41JXMN109186-VIN-001.jpg",
                BlobPathOriginal = "1HGBH41JXMN109186/Checkin/1HGBH41JXMN109186-VIN-001.jpg",
                BlobPathThumbnail = "1HGBH41JXMN109186/Checkin/1HGBH41JXMN109186-VIN-001.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 12345,
                UploadedAtUtc = DateTime.UtcNow,
                SequenceNumber = 1,
                UploaderId = "user-1"
            });
            db.SaveChanges();

            var reloaded = db.Vehicles.Include(v => v.Photos).Single();
            Assert.Single(reloaded.Photos);
            Assert.Equal(Stage.Checkin, reloaded.Photos[0].Stage);
        }

        [Fact]
        public void RejectsDuplicateVin()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            db.Vehicles.Add(new Vehicle { Vin = "SAMEVIN", BlobFolderName = "SAMEVIN", CreatedAtUtc = DateTime.UtcNow });
            db.SaveChanges();

            db.Vehicles.Add(new Vehicle { Vin = "SAMEVIN", BlobFolderName = "SAMEVIN-2", CreatedAtUtc = DateTime.UtcNow });

            Assert.ThrowsAny<Exception>(() => db.SaveChanges());
        }

        [Fact]
        public void AllowsMultipleVehiclesWithNullVin()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            db.Vehicles.Add(new Vehicle { Reg = "REG1", BlobFolderName = "REG1", CreatedAtUtc = DateTime.UtcNow });
            db.Vehicles.Add(new Vehicle { Reg = "REG2", BlobFolderName = "REG2", CreatedAtUtc = DateTime.UtcNow });

            db.SaveChanges(); // Should not throw — both have null Vin.

            Assert.Equal(2, db.Vehicles.Count());
        }
    }
}
