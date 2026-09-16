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
            catch (IOException)
            {
                // A file whose first bytes are a recognisable format signature (e.g. a genuine
                // JPEG SOI marker) but which is truncated or otherwise incomplete -- a corrupted
                // upload, not merely "no EXIF" -- makes MetadataExtractor throw this instead of
                // ImageProcessingException (verified directly against this package version: full
                // garbage throws ImageProcessingException, but a truncated-after-the-header JPEG
                // throws IOException). Same "best effort" contract either way: this method's job
                // is reading a date if one is there, not validating the file, so any failure to
                // parse falls back to null and lets the caller's own image-decode step (which
                // does validate) be the one that rejects the file.
            }

            return null;
        }
    }
}
