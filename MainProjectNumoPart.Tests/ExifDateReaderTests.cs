using System;
using System.IO;
using System.Threading.Tasks;
using MainProjectNumoPart.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class ExifDateReaderTests
    {
        [Fact]
        public async Task TryReadDateTaken_ReturnsDateFromExifDateTimeOriginal()
        {
            using var image = new Image<Rgba32>(50, 50, new Rgba32(10, 20, 30, 255));
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:06:01 09:15:30");
            image.Metadata.ExifProfile = exif;

            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
            stream.Position = 0;

            var result = ExifDateReader.TryReadDateTaken(stream);

            Assert.NotNull(result);
            Assert.Equal(new DateTime(2023, 6, 1, 9, 15, 30, DateTimeKind.Utc), result);
            Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        }

        [Fact]
        public async Task TryReadDateTaken_ReturnsNullWhenJpegHasNoExif()
        {
            using var image = new Image<Rgba32>(50, 50, new Rgba32(10, 20, 30, 255));
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
            stream.Position = 0;

            var result = ExifDateReader.TryReadDateTaken(stream);

            Assert.Null(result);
        }

        [Fact]
        public async Task TryReadDateTaken_ReturnsNullForPng()
        {
            // PNGs never carry EXIF the way JPEGs do — absence is the expected case, not a failure.
            using var image = new Image<Rgba32>(50, 50, new Rgba32(10, 20, 30, 255));
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new PngEncoder());
            stream.Position = 0;

            var result = ExifDateReader.TryReadDateTaken(stream);

            Assert.Null(result);
        }
    }
}
