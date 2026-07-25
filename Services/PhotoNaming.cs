using System;

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
    }
}
