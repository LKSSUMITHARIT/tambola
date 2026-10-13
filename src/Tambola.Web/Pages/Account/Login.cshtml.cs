using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Account;

/// <summary>Manual (Linux) mode login: domain + userID + password. Returns 404 in other modes.</summary>
public class LoginModel : PageModel
{
    private readonly AppOptions _opt;
    private readonly EmployeeService _emps;
    private readonly LoginThrottle _throttle;
    public LoginModel(IOptions<AppOptions> opt, EmployeeService emps, LoginThrottle throttle) { _opt = opt.Value; _emps = emps; _throttle = throttle; }

    public string[] Domains => _opt.AllowedDomains;
    [BindProperty] public string Domain { get; set; } = "";
    [BindProperty] public string UserId { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; private set; }
    public string? ReturnUrl { get; private set; }

    public IActionResult OnGet(string? returnUrl)
    {
        if (!_opt.IsManual) return NotFound();
        if (User.HasClaim(c => c.Type == Claims.EmployeeId)) return LocalRedirect("/");
        ReturnUrl = returnUrl;
        if (Domains.Length == 1) Domain = Domains[0];
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!_opt.IsManual) return NotFound();
        ReturnUrl = returnUrl;
        const string generic = "Invalid domain, user ID or password.";

        if (_emps.BuildAccount(Domain, UserId, out var account) is { } bad) { Error = bad; return Page(); }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (_throttle.IsLocked(account, ip, out var wait))
        {
            Error = $"Too many failed attempts. Try again in {Math.Ceiling(wait.TotalMinutes)} minute(s).";
            return Page();
        }

        var emp = await _emps.VerifyLoginAsync(account, Password);
        if (emp == null) { _throttle.RecordFailure(account, ip); Error = generic; return Page(); }
        _throttle.Clear(account, ip);
        if (!emp.IsActive) { Error = "Your account has been deactivated by an administrator. Please contact the Admin."; return Page(); }

        await ManualSignIn.AsAsync(HttpContext, account);
        var target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return LocalRedirect(emp.MustChangePassword ? "/Account/ChangePassword" : target);
    }
}

public static class ManualSignIn
{
    /// <summary>Issues the Manual-mode cookie for DOMAIN\userid.</summary>
    public static Task AsAsync(HttpContext http, string account)
    {
        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, account) }, CookieAuthenticationDefaults.AuthenticationScheme);
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12) });
    }
}
