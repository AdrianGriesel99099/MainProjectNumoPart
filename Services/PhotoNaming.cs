using System;
using System.Text.RegularExpressions;

namespace MainProjectNumoPart.Services
{
    public static class PhotoNaming
    {
        public static string ResolveBlobFolderName(string? vin, string? reg)
        {
            if (!string.IsNullOrWhiteSpace(vin)) return vin.Trim();
            if (!string.IsNullOrWhiteSpace(reg)) return reg.Trim();
            throw new ArgumentException("At least one of VIN or Reg is required.");
        }

        public static string BuildFileName(string? vin, string? reg, int sequenceNumber, string extension)
        {
            var hasVin = !string.IsNullOrWhiteSpace(vin);
            var hasReg = !string.IsNullOrWhiteSpace(reg);
            var seq = sequenceNumber.ToString("000");

            string identifierPart;
            if (hasVin && hasReg)
                identifierPart = $"{vin!.Trim()}-VIN-{reg!.Trim()}-Reg";
            else if (hasVin)
                identifierPart = $"{vin!.Trim()}-VIN";
            else if (hasReg)
                identifierPart = $"{reg!.Trim()}-Reg";
            else
                throw new ArgumentException("At least one of VIN or Reg is required.");

            return $"{identifierPart}-{seq}{extension}";
        }

        private static readonly Regex UnsafeZipEntryChars = new("[^A-Za-z0-9_.-]+", RegexOptions.Compiled);
        private static readonly Regex ConsecutiveDots = new(@"\.{2,}", RegexOptions.Compiled);

        // VehicleLookupService.NormalizeIdentifier only upper-cases a VIN/Reg and strips
        // whitespace -- it never restricts the character set. A VIN typed as "../../etc/x"
        // reaches this unchanged and ends up embedded in FileName (via BuildFileName above),
        // which the bulk photo-download endpoint then uses directly as a zip entry name. Slashes
        // and backslashes are stripped by the character-class pass below, and ".." runs are
        // collapsed separately (dots alone are otherwise kept, to preserve a file extension) --
        // without both passes a crafted VIN could still leave literal path-traversal segments in
        // a zip entry name, a zip-slip vector for any extractor that doesn't itself guard against
        // it. PhotoExportService.SanitizeForFileName already does the equivalent for the export
        // endpoint's file names; this is that same guard for the plain-download endpoint's zip
        // entries specifically.
        public static string SanitizeForZipEntry(string value)
        {
            var withoutUnsafeChars = UnsafeZipEntryChars.Replace(value, "-");
            var withoutDotRuns = ConsecutiveDots.Replace(withoutUnsafeChars, "-");
            var cleaned = withoutDotRuns.Trim('-');
            return cleaned.Length == 0 ? "photo" : cleaned;
        }
    }
}
