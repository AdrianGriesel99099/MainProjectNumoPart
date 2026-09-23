using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MainProjectNumoPart.Pages.Photos
{
    // [Authorize] only, not role-restricted — Viewers browse and read comments same as anyone
    // else; only the add-comment form is gated (User.CanUpload()) in the markup.
    [Authorize]
    public class ViewModel : PageModel
    {
        private readonly AppDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public ViewModel(AppDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public Photo Photo { get; set; } = null!;
        public string? UploaderEmail { get; set; }
        public List<PhotoComment> CommentsOldestFirst { get; set; } = new();

        // Navigation within the photo's own stage, in the same SequenceNumber order the vehicle
        // page's grid groups and orders photos by (Pages/Vehicles/Details.cshtml.cs) -- so "Next"
        // here always matches what "next" looks like on that grid.
        public int? PreviousPhotoId { get; set; }
        public int? NextPhotoId { get; set; }
        public int PositionInStage { get; set; }
        public int StagePhotoCount { get; set; }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var photo = await _db.Photos
                .Include(p => p.Vehicle)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return NotFound();

            Photo = photo;

            var stagePhotoIds = await _db.Photos
                .Where(p => p.VehicleId == photo.VehicleId && p.Stage == photo.Stage)
                .OrderBy(p => p.SequenceNumber)
                .Select(p => p.Id)
                .ToListAsync();
            var currentIndex = stagePhotoIds.IndexOf(photo.Id);
            PositionInStage = currentIndex + 1;
            StagePhotoCount = stagePhotoIds.Count;
            PreviousPhotoId = currentIndex > 0 ? stagePhotoIds[currentIndex - 1] : null;
            NextPhotoId = currentIndex < stagePhotoIds.Count - 1 ? stagePhotoIds[currentIndex + 1] : null;

            // Best-effort: UploaderId is a loose string with no FK (see UserAdminService.
            // DeleteUserAsync), so the account may no longer exist for an old photo. Falling back
            // to null rather than throwing keeps the page working either way.
            var uploader = await _userManager.FindByIdAsync(photo.UploaderId);
            UploaderEmail = uploader?.Email;

            CommentsOldestFirst = await _db.PhotoComments
                .Where(c => c.PhotoId == id)
                .OrderBy(c => c.CreatedAtUtc)
                .ToListAsync();

            return Page();
        }
    }
}
