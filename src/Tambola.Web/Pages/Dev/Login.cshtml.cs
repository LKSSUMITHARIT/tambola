using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Dev;

/// <summary>DEV ONLY page (404 unless AuthMode=Dev): pick a fake Windows identity.</summary>
public class LoginModel : PageModel
{
    private readonly AppOptions _opt;
    public LoginModel(IOptions<AppOptions> opt) => _opt = opt.Value;

    [BindProperty] public string User_ { get; set; } = "";
    public string? ReturnUrl { get; private set; }

    public IActionResult OnGet(string? returnUrl)
    {
        if (!_opt.AuthMode.Equals("Dev", StringComparison.OrdinalIgnoreCase)) return NotFound();
        ReturnUrl = returnUrl;
        return Page();
    }

    public IActionResult OnPost(string? returnUrl)
    {
        if (!_opt.AuthMode.Equals("Dev", StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (string.IsNullOrWhiteSpace(User_)) return Page();
        Response.Cookies.Append(DevAuthHandler.Cookie, User_.Trim(), new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax });
        return LocalRedirect(string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? "/" : returnUrl);
    }
}
