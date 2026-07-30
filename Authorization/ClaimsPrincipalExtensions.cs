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
    }
}
