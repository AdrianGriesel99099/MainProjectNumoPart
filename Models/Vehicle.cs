using System.Collections.Generic;

namespace MainProjectNumoPart.Models
{
    public class Vehicle
    {
        public int Id { get; set; }
        public string? Vin { get; set; }
        public string? Reg { get; set; }
        public string? MakeModel { get; set; }
        public string BlobFolderName { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }

        public List<Photo> Photos { get; set; } = new();
        public List<VehicleUpdate> VehicleUpdates { get; set; } = new();
        public List<DamageMark> DamageMarks { get; set; } = new();
    }
}
