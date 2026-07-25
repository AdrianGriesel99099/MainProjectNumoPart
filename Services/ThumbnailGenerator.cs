// Services/ThumbnailGenerator.cs
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace MainProjectNumoPart.Services
{
    public static class ThumbnailGenerator
    {
        private const int MaxDimension = 400;

        // Always JPEG, regardless of the source format — this is why BlobPathThumbnail
        // can differ in extension from BlobPathOriginal for PNG uploads.
        public static async Task<MemoryStream> CreateThumbnailAsync(Stream sourceImage, CancellationToken ct = default)
        {
            using var image = await Image.LoadAsync(sourceImage, ct);

            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxDimension, MaxDimension)
            }));

            var output = new MemoryStream();
            await image.SaveAsync(output, new JpegEncoder { Quality = 80 }, ct);
            output.Position = 0;
            return output;
        }
    }
}
