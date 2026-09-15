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
            // Not every JPEG carries parseable EXIF, and PNGs never do — absence is the expected
            // case, not a failure. ImageProcessingException covers a genuinely unsupported format,
            // but a JPEG with a truncated or malformed EXIF/IFD segment (a corrupt camera output,
            // or a file re-encoded by some other app) can trip MetadataExtractor's own IO/format
            // exceptions while parsing that segment. Either way this method's whole contract is
            // "best-effort taken date, null if it can't be read" — Upload.cshtml.cs has no
            // surrounding try/catch of its own, so letting anything else escape here fails the
            // entire upload batch instead of just showing "Not recorded" for this one photo.
            catch (Exception ex) when (ex is ImageProcessingException or IOException or FormatException
                or IndexOutOfRangeException or ArgumentException)
            {
            }

            return null;
        }
    }
}
