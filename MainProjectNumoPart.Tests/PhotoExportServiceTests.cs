using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharpCore.Pdf.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class PhotoExportServiceTests
    {
        private static PhotoExportService Build(AppDbContext db, FakePhotoStorage storage)
            => new(db, storage, NullLogger<PhotoExportService>.Instance);

        private static Vehicle SeedVehicle(AppDbContext db, string reg = "AB12CDE")
        {
            var v = new Vehicle { Reg = reg, BlobFolderName = reg, CreatedAtUtc = DateTime.UtcNow };
            db.Vehicles.Add(v);
            db.SaveChanges();
            return v;
        }

        private static async Task<byte[]> CreateJpegAsync(int width, int height, Rgba32 color)
        {
            using var image = new Image<Rgba32>(width, height, color);
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new JpegEncoder { Quality = 95 });
            return stream.ToArray();
        }

        private static async Task<Photo> SeedPhotoAsync(
            AppDbContext db, FakePhotoStorage storage, Vehicle vehicle,
            Stage stage = Stage.Checkin, Part? part = null, int sequenceNumber = 1,
            long? sizeBytesOverride = null)
        {
            var bytes = await CreateJpegAsync(40, 30, new Rgba32(10, 100, 200, 255));
            var blobPath = $"{vehicle.BlobFolderName}/{stage}/p{sequenceNumber}.jpg";
            storage.Originals[blobPath] = bytes;

            var photo = new Photo
            {
                VehicleId = vehicle.Id,
                Stage = stage,
                Part = part,
                FileName = $"p{sequenceNumber}.jpg",
                BlobPathOriginal = blobPath,
                BlobPathThumbnail = blobPath + "-t",
                ContentType = "image/jpeg",
                SizeBytes = sizeBytesOverride ?? bytes.Length,
                UploadedAtUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
                SequenceNumber = sequenceNumber,
                UploaderId = "u1"
            };
            db.Photos.Add(photo);
            db.SaveChanges();
            return photo;
        }

        [Fact]
        public async Task ExportAsync_NoIdsGiven_ReturnsNoPhotosSelected()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();

            var result = await Build(db, storage).ExportAsync(Array.Empty<int>(), PhotoExportFormat.Zip, false);

            Assert.Equal(PhotoExportStatus.NoPhotosSelected, result.Status);
            Assert.Null(result.Content);
        }

        [Fact]
        public async Task ExportAsync_OnlyUnknownIds_ReturnsNoPhotosSelected()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();

            var result = await Build(db, storage).ExportAsync(new[] { 999 }, PhotoExportFormat.Zip, false);

            Assert.Equal(PhotoExportStatus.NoPhotosSelected, result.Status);
        }

        [Fact]
        public async Task ExportAsync_TooManyIds_ReturnsTooManySelected()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var tooMany = Enumerable.Range(1, PhotoExportService.MaxPhotosPerRequest + 1).ToArray();

            var result = await Build(db, storage).ExportAsync(tooMany, PhotoExportFormat.Zip, false);

            Assert.Equal(PhotoExportStatus.TooManySelected, result.Status);
        }

        [Fact]
        public async Task ExportAsync_TotalBytesOverLimit_ReturnsTooLarge()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var photo = await SeedPhotoAsync(db, storage, vehicle, sizeBytesOverride: 600L * 1024 * 1024);

            var result = await Build(db, storage).ExportAsync(new[] { photo.Id }, PhotoExportFormat.Zip, false);

            Assert.Equal(PhotoExportStatus.TooLarge, result.Status);
        }

        [Fact]
        public async Task ExportAsync_Zip_ContainsOneImageAndOneTextEntryPerPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var p1 = await SeedPhotoAsync(db, storage, vehicle, Stage.Checkin, Part.FrontBumper, 1);
            var p2 = await SeedPhotoAsync(db, storage, vehicle, Stage.Checkout, null, 2);

            var result = await Build(db, storage).ExportAsync(new[] { p1.Id, p2.Id }, PhotoExportFormat.Zip, false);

            Assert.Equal(PhotoExportStatus.Success, result.Status);
            Assert.Equal("application/zip", result.ContentType);
            Assert.EndsWith(".zip", result.FileName);

            using var archive = new ZipArchive(new MemoryStream(result.Content!), ZipArchiveMode.Read);
            Assert.Equal(4, archive.Entries.Count);
            Assert.Contains(archive.Entries, e => e.FullName == "Checkin/Checkin_FrontBumper_001.jpg");
            Assert.Contains(archive.Entries, e => e.FullName == "Checkin/Checkin_FrontBumper_001.txt");
            Assert.Contains(archive.Entries, e => e.FullName == "Checkout/Checkout_Untagged_002.jpg");
            Assert.Contains(archive.Entries, e => e.FullName == "Checkout/Checkout_Untagged_002.txt");
        }

        [Fact]
        public async Task ExportAsync_Zip_TextEntryCarriesStagePartDateAndComments()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var photo = await SeedPhotoAsync(db, storage, vehicle, Stage.Progress, Part.Bonnet, 3);
            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photo.Id, AuthorId = "u2", AuthorEmail = "staff@workshop.local",
                Body = "Dent found near the latch.", CreatedAtUtc = new DateTime(2026, 9, 2, 8, 30, 0, DateTimeKind.Utc)
            });
            db.SaveChanges();

            var result = await Build(db, storage).ExportAsync(new[] { photo.Id }, PhotoExportFormat.Zip, false);

            using var archive = new ZipArchive(new MemoryStream(result.Content!), ZipArchiveMode.Read);
            var textEntry = archive.Entries.Single(e => e.FullName.EndsWith(".txt"));
            using var reader = new StreamReader(textEntry.Open());
            var text = reader.ReadToEnd();

            Assert.Contains("Stage: Progress", text);
            Assert.Contains("Part: Bonnet", text);
            Assert.Contains("1 Sep 2026", text);
            Assert.Contains("staff@workshop.local", text);
            Assert.Contains("Dent found near the latch.", text);
        }

        [Fact]
        public async Task ExportAsync_Zip_NoComments_SaysSoRatherThanOmittingSection()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var photo = await SeedPhotoAsync(db, storage, vehicle);

            var result = await Build(db, storage).ExportAsync(new[] { photo.Id }, PhotoExportFormat.Zip, false);

            using var archive = new ZipArchive(new MemoryStream(result.Content!), ZipArchiveMode.Read);
            var textEntry = archive.Entries.Single(e => e.FullName.EndsWith(".txt"));
            using var reader = new StreamReader(textEntry.Open());
            Assert.Contains("No comments.", reader.ReadToEnd());
        }

        [Fact]
        public async Task ExportAsync_Zip_IncludeDamageMarksTrue_BurnsPinIntoImage()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var photo = await SeedPhotoAsync(db, storage, vehicle, Stage.Checkin, Part.FrontBumper, 1);
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = vehicle.Id, Part = Part.FrontBumper, PhotoId = photo.Id,
                XPercent = 50, YPercent = 50, Note = "Scratch", AuthorId = "u1",
                AuthorEmail = "a@b.local", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var result = await Build(db, storage).ExportAsync(new[] { photo.Id }, PhotoExportFormat.Zip, includeDamageMarks: true);

            using var archive = new ZipArchive(new MemoryStream(result.Content!), ZipArchiveMode.Read);
            var imageEntry = archive.Entries.Single(e => e.FullName.EndsWith(".jpg"));
            using var ms = new MemoryStream();
            imageEntry.Open().CopyTo(ms);
            using var decoded = Image.Load<Rgba32>(ms.ToArray());
            var center = decoded[20, 15]; // 40x30 image, 50%/50% -> (20,15)
            Assert.True(center.R > 180 && center.B < 100, $"expected a burned-in red pin, got {center}");
        }

        [Fact]
        public async Task ExportAsync_Zip_IncludeDamageMarksFalse_LeavesImageUnpinned()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var photo = await SeedPhotoAsync(db, storage, vehicle, Stage.Checkin, Part.FrontBumper, 1);
            db.DamageMarks.Add(new DamageMark
            {
                VehicleId = vehicle.Id, Part = Part.FrontBumper, PhotoId = photo.Id,
                XPercent = 50, YPercent = 50, Note = "Scratch", AuthorId = "u1",
                AuthorEmail = "a@b.local", CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var result = await Build(db, storage).ExportAsync(new[] { photo.Id }, PhotoExportFormat.Zip, includeDamageMarks: false);

            using var archive = new ZipArchive(new MemoryStream(result.Content!), ZipArchiveMode.Read);
            var imageEntry = archive.Entries.Single(e => e.FullName.EndsWith(".jpg"));
            using var ms = new MemoryStream();
            imageEntry.Open().CopyTo(ms);
            using var decoded = Image.Load<Rgba32>(ms.ToArray());
            var center = decoded[20, 15];
            Assert.False(center.R > 180 && center.B < 100, $"did not expect a pin when includeDamageMarks is false, got {center}");
        }

        [Fact]
        public async Task ExportAsync_Pdf_ProducesOnePagePerPhoto()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var p1 = await SeedPhotoAsync(db, storage, vehicle, Stage.Checkin, Part.FrontBumper, 1);
            var p2 = await SeedPhotoAsync(db, storage, vehicle, Stage.Checkout, null, 2);
            db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = p1.Id, AuthorId = "u2", AuthorEmail = "staff@workshop.local",
                Body = "A fairly long comment that should still render without throwing, even when it needs to wrap across several lines inside the generated PDF page.",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();

            var result = await Build(db, storage).ExportAsync(new[] { p1.Id, p2.Id }, PhotoExportFormat.Pdf, includeDamageMarks: false);

            Assert.Equal(PhotoExportStatus.Success, result.Status);
            Assert.Equal("application/pdf", result.ContentType);
            Assert.EndsWith(".pdf", result.FileName);

            using var document = PdfReader.Open(new MemoryStream(result.Content!), PdfDocumentOpenMode.ReadOnly);
            Assert.True(document.PageCount >= 2, "expected at least one page per photo");
        }

        [Fact]
        public async Task ExportAsync_Pdf_ManyLongCommentsDoNotThrow_AndAddExtraPages()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var storage = new FakePhotoStorage();
            var vehicle = SeedVehicle(db);
            var photo = await SeedPhotoAsync(db, storage, vehicle);
            for (var i = 0; i < 40; i++)
            {
                db.PhotoComments.Add(new PhotoComment
                {
                    PhotoId = photo.Id, AuthorId = "u2", AuthorEmail = $"staff{i}@workshop.local",
                    Body = new string('x', 400),
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(i)
                });
            }
            db.SaveChanges();

            var result = await Build(db, storage).ExportAsync(new[] { photo.Id }, PhotoExportFormat.Pdf, includeDamageMarks: false);

            using var document = PdfReader.Open(new MemoryStream(result.Content!), PdfDocumentOpenMode.ReadOnly);
            Assert.True(document.PageCount > 1, "a photo with many long comments should overflow onto extra pages rather than being cut off silently");
        }
    }
}
