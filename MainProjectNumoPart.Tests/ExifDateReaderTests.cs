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

        // A truncated or malformed EXIF/IFD segment in an otherwise-valid JPEG can trip
        // MetadataExtractor's own IO/format exceptions while parsing that segment, not just
        // ImageProcessingException (which only covers a genuinely unsupported format). Reproduced
        // here with a stream whose Read always throws, rather than hand-crafting a specific
        // corrupt byte sequence MetadataExtractor happens to choke on today — what matters is
        // this method's contract ("best-effort date, null if it can't be read"), not which
        // internal exception type a particular malformed file trips. Upload.cshtml.cs has no
        // try/catch of its own around this call, so anything besides ImageProcessingException
        // escaping here used to fail the entire upload batch instead of leaving just this one
        // photo's "Taken" as "Not recorded".
        [Fact]
        public void TryReadDateTaken_ReturnsNullWhenParsingThrowsANonImageProcessingException()
        {
            using var stream = new ThrowingStream(new IndexOutOfRangeException("simulated malformed IFD offset"));

            var result = ExifDateReader.TryReadDateTaken(stream);

            Assert.Null(result);
        }

        private sealed class ThrowingStream : MemoryStream
        {
            private readonly Exception _exception;

            public ThrowingStream(Exception exception) : base(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 })
            {
                _exception = exception;
            }

            public override int Read(byte[] buffer, int offset, int count) => throw _exception;

            public override int ReadByte() => throw _exception;
        }
    }
}
