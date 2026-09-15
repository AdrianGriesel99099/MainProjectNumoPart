using System.Security.Claims;

namespace MainProjectNumoPart.Authorization
{
    // Keeps the "who is allowed to do what" rules in one place instead of scattering
    // IsInRole(...) || IsInRole(...) across views, where a missed site silently shows a
    // control that leads straight to an access-denied page.
    public static class ClaimsPrincipalExtensions
    {
        public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin);

        // Mirrors [Authorize(Roles = Roles.StaffOrAdmin)] on the Upload page. These two must
        // agree: if they drift, the nav link and the page it points at disagree and a user is
        // shown a link they can't use. Change both together, or neither.
        public static bool CanUpload(this ClaimsPrincipal user)
            => user.IsInRole(Roles.Staff) || user.IsInRole(Roles.Admin);

        // A comment, job-card update, or damage-mark note could previously only ever be removed
        // by an Admin -- the author who wrote it, even seconds ago with a typo, had no way to
        // undo it themselves and had to ask an Admin to do it for them. This mirrors the AddAsync
        // side (Staff-or-Admin can post) onto the delete side: the author can always remove their
        // own, Admins can still remove anyone's.
        public static bool CanDeleteNote(this ClaimsPrincipal user, string authorId)
            => user.IsAdmin() || user.CurrentUserId() == authorId;

        // Razor views can't call ClaimsPrincipal.FindFirstValue directly — it lives in
        // Microsoft.AspNetCore.Http, which .cshtml compilation's default usings don't include
        // (unlike .cs files, which get it for free from this Web SDK project's implicit usings).
        public static string? CurrentUserId(this ClaimsPrincipal user)
            => user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
