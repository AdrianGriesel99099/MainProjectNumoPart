using System.IO;
using System.Threading.Tasks;
using MainProjectNumoPart.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class ThumbnailGeneratorTests
    {
        private static async Task<MemoryStream> CreateTestImageAsync(int width, int height, bool asPng)
        {
            using var image = new Image<Rgba32>(width, height, new Rgba32(120, 60, 200, 255));
            var stream = new MemoryStream();
            if (asPng)
                await image.SaveAsync(stream, new PngEncoder());
            else
                await image.SaveAsync(stream, new JpegEncoder { Quality = 90 });
            stream.Position = 0;
            return stream;
        }

        [Fact]
        public async Task CreateThumbnailAsync_JpegInput_ProducesJpegOutput()
        {
            using var source = await CreateTestImageAsync(100, 100, asPng: false);

            using var thumbnail = await ThumbnailGenerator.CreateThumbnailAsync(source);

            var bytes = thumbnail.ToArray();
            Assert.True(bytes.Length > 2);
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]); // JPEG SOI marker
        }

        [Fact]
        public async Task CreateThumbnailAsync_PngInput_ProducesJpegOutputNotPng()
        {
            // The whole reason BlobPathThumbnail can differ in extension from BlobPathOriginal
            // for a PNG upload: thumbnails are always JPEG, regardless of the source format.
            using var source = await CreateTestImageAsync(100, 100, asPng: true);

            using var thumbnail = await ThumbnailGenerator.CreateThumbnailAsync(source);

            var bytes = thumbnail.ToArray();
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]); // JPEG SOI marker, not PNG's 0x89 0x50 0x4E 0x47

            var detectedFormat = Image.DetectFormat(bytes);
            Assert.Equal("JPEG", detectedFormat.Name);
        }

        [Fact]
        public async Task CreateThumbnailAsync_LargeImage_ResizedToFitWithinMaxDimension()
        {
            using var source = await CreateTestImageAsync(1000, 600, asPng: false);

            using var thumbnail = await ThumbnailGenerator.CreateThumbnailAsync(source);

            using var decoded = Image.Load(thumbnail.ToArray());
            Assert.True(decoded.Width <= 400);
            Assert.True(decoded.Height <= 400);
            // 1000x600 is landscape (5:3) — Max mode fits the longer side (width) to the 400 cap.
            Assert.Equal(400, decoded.Width);
            Assert.Equal(240, decoded.Height);
        }

        [Fact]
        public async Task CreateThumbnailAsync_SmallImage_IsUpscaledToFillMaxDimension()
        {
            using var source = await CreateTestImageAsync(100, 100, asPng: false);

            using var thumbnail = await ThumbnailGenerator.CreateThumbnailAsync(source);

            using var decoded = Image.Load(thumbnail.ToArray());
            // Verified empirically (this assertion replaced an incorrect assumption that Max mode
            // only shrinks): SixLabors.ImageSharp 3.1.12's ResizeMode.Max scales a source SMALLER
            // than the target bounds UP to fill them too, preserving aspect ratio either way. A
            // 100x100 source targeting a 400x400 box comes out at 400x400, not left at 100x100.
            Assert.Equal(400, decoded.Width);
            Assert.Equal(400, decoded.Height);
        }
    }
}
