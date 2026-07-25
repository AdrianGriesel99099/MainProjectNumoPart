namespace MainProjectNumoPart.Models
{
    public class Photo
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; } = null!;
        public Stage Stage { get; set; }
        public string FileName { get; set; } = null!;
        public string BlobPathOriginal { get; set; } = null!;
        public string BlobPathThumbnail { get; set; } = null!;
        public string ContentType { get; set; } = null!;
        public long SizeBytes { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public DateTime? DateTakenUtc { get; set; }
        public int SequenceNumber { get; set; }
        public string UploaderId { get; set; } = null!;
    }
}
