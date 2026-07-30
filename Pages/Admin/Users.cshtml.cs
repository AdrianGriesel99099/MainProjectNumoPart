// Pages/Admin/Users.cshtml.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.Tasks;
using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Admin
{
    [Authorize(Roles = Roles.Admin)]
    public class UsersModel : PageModel
    {
        private readonly UserAdminService _users;

        public UsersModel(UserAdminService users)
        {
            _users = users;
        }

        public List<UserSummary> Users { get; set; } = new();

        public string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        [BindProperty]
        public InputModel Input { get; set; } = new();

        // TempData rather than a plain property: every POST ends in a redirect (Post/Redirect/Get),
        // so the message has to survive that redirect. Needs no extra services — AddRazorPages
        // registers a cookie-backed TempData provider by default.
        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public bool StatusIsError { get; set; }

        public class InputModel
        {
            [Required, EmailAddress]
            public string Email { get; set; } = "";

            // DataType.Password makes asp-for render type="password". Without it the tag helper
            // emits a plain text box and the temporary password is visible on screen as it's typed.
            [Required, MinLength(8), DataType(DataType.Password)]
            [Display(Name = "Temporary password")]
            public string TemporaryPassword { get; set; } = "";

            // Least privilege by default: an admin has to deliberately choose to grant more.
            [Required]
            public string Role { get; set; } = Roles.Viewer;
        }

        public async Task OnGetAsync()
        {
            Users = await _users.ListUsersAsync();
        }

        public async Task<IActionResult> OnPostCreateAsync()
        {
            if (!ModelState.IsValid)
            {
                // Every return Page() must repopulate Users first, or the table renders empty
                // behind the validation errors.
                Users = await _users.ListUsersAsync();
                return Page();
            }

            var result = await _users.CreateUserAsync(Input.Email, Input.TemporaryPassword, Input.Role);

            if (result.IsError)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                Users = await _users.ListUsersAsync();
                return Page();
            }

            StatusMessage = result.Message;
            StatusIsError = false;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostChangeRoleAsync(string userId, string role)
        {
            var result = await _users.ChangeRoleAsync(CurrentUserId, userId, role);

            StatusMessage = result.Message;
            StatusIsError = result.IsError;

            // Redirect on failure too: a rejected role change left in the browser's resubmit
            // buffer would re-fire on refresh, and the guard messages read the same either way.
            return RedirectToPage();
        }
    }
}
