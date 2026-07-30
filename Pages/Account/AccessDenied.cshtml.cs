// Pages/Account/AccessDenied.cshtml.cs
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Account
{
    // Deliberately NOT [Authorize]. The visitor here has already failed an authorization check,
    // and may be anonymous — requiring auth would bounce them to the login page and, on a bad
    // day, into a redirect loop.
    public class AccessDeniedModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
