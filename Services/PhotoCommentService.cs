using System;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    // Comments on an individual photo — see NoteResult for why this shares its result type with
    // VehicleUpdateService, and PhotoComment.AuthorEmail for why authorship is captured at write
    // time rather than resolved live.
    public class PhotoCommentService
    {
        public const int MaxBodyLength = 2000;

        private readonly AppDbContext _db;
        private readonly ILogger<PhotoCommentService> _logger;

        public PhotoCommentService(AppDbContext db, ILogger<PhotoCommentService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<NoteResult> AddAsync(
            int photoId, string? body, string authorId, string authorEmail, CancellationToken ct = default)
        {
            var trimmed = body?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return new NoteResult(NoteStatus.EmptyBody, "Enter some text before posting.");
            }
            if (trimmed.Length > MaxBodyLength)
            {
                return new NoteResult(NoteStatus.TooLong, $"Comments are limited to {MaxBodyLength} characters.");
            }

            var photoExists = await _db.Photos.AnyAsync(p => p.Id == photoId, ct);
            if (!photoExists)
            {
                return new NoteResult(NoteStatus.NotFound);
            }

            _db.PhotoComments.Add(new PhotoComment
            {
                PhotoId = photoId,
                AuthorId = authorId,
                AuthorEmail = authorEmail,
                Body = trimmed,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Photo comment added on photo {PhotoId} by {AuthorId}", photoId, authorId);
            return new NoteResult(NoteStatus.Success);
        }

        public async Task<NoteResult> DeleteAsync(int commentId, CancellationToken ct = default)
        {
            var comment = await _db.PhotoComments.FindAsync(new object[] { commentId }, ct);
            if (comment is null)
            {
                return new NoteResult(NoteStatus.NotFound);
            }

            _db.PhotoComments.Remove(comment);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Photo comment {CommentId} deleted", commentId);
            return new NoteResult(NoteStatus.Success);
        }
    }
}
