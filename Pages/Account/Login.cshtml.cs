using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Competition.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Competition.Pages.Account;

public sealed class LoginModel(AdminCredentialValidator credentialValidator) : PageModel
{
    [BindProperty]
    public CredentialsInput Credentials { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!credentialValidator.IsValid(Credentials.Username, Credentials.Password))
        {
            ModelState.AddModelError(string.Empty, "Neplatné uživatelské jméno nebo heslo.");
            return Page();
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, Credentials.Username),
            new Claim(ClaimTypes.Role, "Admin")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return LocalRedirect(GetSafeReturnUrl());
    }

    private string GetSafeReturnUrl() =>
        Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Page("/Editions/Index")!;

    public sealed class CredentialsInput
    {
        [Required(ErrorMessage = "Zadejte uživatelské jméno.")]
        [Display(Name = "Uživatelské jméno")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Zadejte heslo.")]
        [DataType(DataType.Password)]
        [Display(Name = "Heslo")]
        public string Password { get; set; } = string.Empty;
    }
}
