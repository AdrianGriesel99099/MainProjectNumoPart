// Pages/Admin/CreateUser.cshtml.cs
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MainProjectNumoPart.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class CreateUserModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;

        public CreateUserModel(UserManager<IdentityUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? SuccessMessage { get; set; }

        public class InputModel
        {
            [Required, EmailAddress]
            public string Email { get; set; } = "";

            [Required, MinLength(8)]
            public string TemporaryPassword { get; set; } = "";
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = new IdentityUser { UserName = Input.Email, Email = Input.Email, EmailConfirmed = true };
            var result = await _userManager.CreateAsync(user, Input.TemporaryPassword);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return Page();
            }

            SuccessMessage = $"Account created for {Input.Email}.";
            Input = new InputModel();
            return Page();
        }
    }
}
