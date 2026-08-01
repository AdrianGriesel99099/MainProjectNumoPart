namespace MainProjectNumoPart.Models
{
    // A free-text entry on a vehicle's job card — separate from photos, for "what's been done /
    // what's outstanding" notes about the job itself.
    public class VehicleUpdate
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; } = null!;
        public string AuthorId { get; set; } = null!;

        // See PhotoComment.AuthorEmail for why this is captured at write time, not resolved live.
        public string AuthorEmail { get; set; } = null!;

        public string Body { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
