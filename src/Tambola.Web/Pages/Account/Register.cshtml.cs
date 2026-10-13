using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Account;

/// <summary>Manual (Linux) mode registration: domain, userID, password, name, Employee Code, email. 404 in other modes.</summary>
public class RegisterModel : PageModel
{
    private readonly AppOptions _opt;
    private readonly EmployeeService _emps;
    public RegisterModel(IOptions<AppOptions> opt, EmployeeService emps) { _opt = opt.Value; _emps = emps; }

    public string[] Domains => _opt.AllowedDomains;
    public int MinLength => _opt.PasswordMinLength;
    [BindProperty] public string Domain { get; set; } = "";
    [BindProperty] public string UserId { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string EmployeeCode { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!_opt.IsManual) return NotFound();
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.HasClaim(c => c.Type == Tambola.Web.Auth.Claims.EmployeeId)) return LocalRedirect("/");
            // signed in but deactivated/removed: drop the cookie so they are not bounced around
            await HttpContext.SignOutAsync(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
            return LocalRedirect("/Account/Login");
        }
        if (Domains.Length == 1) Domain = Domains[0];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_opt.IsManual) return NotFound();
        if (_emps.BuildAccount(Domain, UserId, out var account) is { } bad) { Error = bad; return Page(); }
        if (Password != ConfirmPassword) { Error = "The two passwords do not match."; return Page(); }
        Error = await _emps.RegisterAsync(account, DisplayName, EmployeeCode, Email, Password);
        if (Error != null) return Page();
        await ManualSignIn.AsAsync(HttpContext, account);
        return LocalRedirect("/");
    }
}
