using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace MainProjectNumoPart.Services
{
    // Burns each damage mark's pin directly into the image's pixels — used by photo export so the
    // pin survives in a plain JPEG (a zip entry, or embedded in a PDF page) with no client-side
    // canvas to draw it. Pixel manipulation is done by hand (no SixLabors.ImageSharp.Drawing
    // dependency) since a filled circle is the only shape ever needed here.
    public static class DamageMarkImageAnnotator
    {
        private static readonly Rgba32 PinFill = new(220, 30, 30, 255);
        private static readonly Rgba32 PinRing = new(255, 255, 255, 255);

        public static async Task<byte[]> BurnPinsAsync(
            byte[] imageBytes, IReadOnlyList<(double XPercent, double YPercent)> pins, CancellationToken ct = default)
        {
            using var image = await Image.LoadAsync<Rgba32>(new MemoryStream(imageBytes), ct);

            // Scales with image size (small thumbnaily uploads still get a visible pin, large
            // ones don't get a pinprick) but never shrinks below a size that reads on screen.
            var radius = Math.Max(6, Math.Min(image.Width, image.Height) / 30);

            foreach (var (xPercent, yPercent) in pins)
            {
                DrawPin(image, xPercent, yPercent, radius);
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new JpegEncoder { Quality = 90 }, ct);
            return output.ToArray();
        }

        private static void DrawPin(Image<Rgba32> image, double xPercent, double yPercent, int radius)
        {
            var cx = (int)Math.Round(image.Width * xPercent / 100.0);
            var cy = (int)Math.Round(image.Height * yPercent / 100.0);
            var ringRadius = radius + 2;

            var minX = Math.Max(0, cx - ringRadius);
            var maxX = Math.Min(image.Width - 1, cx + ringRadius);
            var minY = Math.Max(0, cy - ringRadius);
            var maxY = Math.Min(image.Height - 1, cy + ringRadius);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared <= radius * radius)
                    {
                        image[x, y] = PinFill;
                    }
                    else if (distanceSquared <= ringRadius * ringRadius)
                    {
                        image[x, y] = PinRing;
                    }
                }
            }
        }
    }
}
