using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MainProjectNumoPart.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class DamageMarkImageAnnotatorTests
    {
        private static async Task<byte[]> CreateSolidJpegAsync(int width, int height, Rgba32 color)
        {
            using var image = new Image<Rgba32>(width, height, color);
            using var stream = new MemoryStream();
            await image.SaveAsync(stream, new JpegEncoder { Quality = 95 });
            return stream.ToArray();
        }

        [Fact]
        public async Task BurnPinsAsync_DrawsRedPinAtRequestedPercentPosition()
        {
            var blue = new Rgba32(0, 0, 255, 255);
            var original = await CreateSolidJpegAsync(200, 200, blue);

            var result = await DamageMarkImageAnnotator.BurnPinsAsync(
                original, new List<(double XPercent, double YPercent)> { (50, 50) });

            using var decoded = Image.Load<Rgba32>(result);
            var center = decoded[100, 100];
            Assert.True(center.R > 180 && center.B < 100, $"expected a red pin at center, got {center}");

            // Far from the pin, the original blue should survive re-encoding untouched.
            var corner = decoded[5, 5];
            Assert.True(corner.B > 180 && corner.R < 100, $"expected untouched blue at corner, got {corner}");
        }

        [Fact]
        public async Task BurnPinsAsync_MultiplePins_AllDrawn()
        {
            var white = new Rgba32(255, 255, 255, 255);
            var original = await CreateSolidJpegAsync(300, 100, white);

            var result = await DamageMarkImageAnnotator.BurnPinsAsync(
                original, new List<(double XPercent, double YPercent)> { (10, 50), (90, 50) });

            using var decoded = Image.Load<Rgba32>(result);
            var leftPin = decoded[30, 50];
            var rightPin = decoded[270, 50];
            Assert.True(leftPin.R > 180 && leftPin.G < 100, $"expected a red pin near the left edge, got {leftPin}");
            Assert.True(rightPin.R > 180 && rightPin.G < 100, $"expected a red pin near the right edge, got {rightPin}");
        }

        [Fact]
        public async Task BurnPinsAsync_NoPins_ReturnsUnchangedImage()
        {
            var green = new Rgba32(0, 255, 0, 255);
            var original = await CreateSolidJpegAsync(100, 100, green);

            var result = await DamageMarkImageAnnotator.BurnPinsAsync(
                original, new List<(double XPercent, double YPercent)>());

            using var decoded = Image.Load<Rgba32>(result);
            var pixel = decoded[50, 50];
            Assert.True(pixel.G > 180 && pixel.R < 100, $"expected untouched green, got {pixel}");
        }

        [Fact]
        public async Task BurnPinsAsync_OutputIsValidJpeg()
        {
            var original = await CreateSolidJpegAsync(50, 50, new Rgba32(10, 20, 30, 255));

            var result = await DamageMarkImageAnnotator.BurnPinsAsync(
                original, new List<(double XPercent, double YPercent)> { (0, 0) });

            Assert.Equal(0xFF, result[0]);
            Assert.Equal(0xD8, result[1]); // JPEG SOI marker
        }
    }
}
