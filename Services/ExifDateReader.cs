// Services/ExifDateReader.cs
using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace MainProjectNumoPart.Services
{
    public static class ExifDateReader
    {
        public static DateTime? TryReadDateTaken(Stream imageStream)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(imageStream);
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

                if (subIfd is not null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
                {
                    return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                }
            }
            catch (ImageProcessingException)
            {
                // Not every JPEG carries parseable EXIF, and PNGs never do —
                // absence is the expected case, not a failure.
            }

            return null;
        }
    }
}
