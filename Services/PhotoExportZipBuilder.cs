using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MainProjectNumoPart.Services
{
    // Builds the "zip of JPEGs" export format: one image plus one sidecar .txt per photo, grouped
    // into per-stage folders the same way the plain bulk-download endpoint groups its zip.
    public static class PhotoExportZipBuilder
    {
        public static byte[] Build(IReadOnlyList<PhotoExportItem> items)
        {
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in items)
                {
                    var folder = item.Stage.ToString();

                    // Already JPEG-encoded by PhotoExportService — no further compression benefit.
                    var imageEntry = archive.CreateEntry($"{folder}/{item.FileNameBase}.jpg", CompressionLevel.NoCompression);
                    using (var entryStream = imageEntry.Open())
                    {
                        entryStream.Write(item.ImageBytes, 0, item.ImageBytes.Length);
                    }

                    var textEntry = archive.CreateEntry($"{folder}/{item.FileNameBase}.txt", CompressionLevel.Optimal);
                    using (var entryStream = textEntry.Open())
                    using (var writer = new StreamWriter(entryStream))
                    {
                        WriteDetails(writer, item);
                    }
                }
            }

            return memoryStream.ToArray();
        }

        private static void WriteDetails(TextWriter writer, PhotoExportItem item)
        {
            var partLabel = item.Part.HasValue ? PartDisplayName.Get(item.Part.Value) : "Untagged";
            writer.WriteLine($"Stage: {item.Stage}");
            writer.WriteLine($"Part: {partLabel}");
            writer.WriteLine($"Date: {item.UploadedAtUtc:d MMM yyyy, HH:mm} UTC");
            writer.WriteLine();

            if (item.Comments.Count == 0)
            {
                writer.WriteLine("No comments.");
                return;
            }

            writer.WriteLine("Comments:");
            foreach (var comment in item.Comments)
            {
                writer.WriteLine($"- {comment.AuthorEmail} ({comment.CreatedAtUtc:d MMM yyyy, HH:mm}): {comment.Body}");
            }
        }
    }
}
