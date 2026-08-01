namespace MainProjectNumoPart.Models
{
    public class PhotoComment
    {
        public int Id { get; set; }
        public int PhotoId { get; set; }
        public Photo Photo { get; set; } = null!;
        public string AuthorId { get; set; } = null!;

        // Captured at write time rather than resolved from AuthorId at read time. A comment is a
        // readable log by design — if the author's account is later deleted (UserAdminService
        // .DeleteUserAsync), every comment they ever wrote must still show who wrote it. Storing
        // the email once makes that permanent with no join and no "(deleted user)" placeholder.
        public string AuthorEmail { get; set; } = null!;

        public string Body { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
