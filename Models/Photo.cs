namespace MainProjectNumoPart.Models
{
    public class Photo
    {
        public int Id { get; set; }
        public int VehicleId { get; set; }
        public Vehicle Vehicle { get; set; } = null!;
        public Stage Stage { get; set; }

        // Which panel of the car this photo shows. Null = not tagged yet, which is the normal
        // state right after a bulk upload — staff shoot a burst and sort them afterwards.
        // Deliberately NOT part of the blob path: paths are {folder}/{Stage}/{file}, so putting
        // Part in there would mean copying blobs every time someone corrected a tag.
        public Part? Part { get; set; }

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
