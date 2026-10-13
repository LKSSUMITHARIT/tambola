using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Account;

/// <summary>Change password (required after an Admin reset) and Logout. Manual mode only.</summary>
public class ChangePasswordModel : PageModel
{
    private readonly AppOptions _opt;
    private readonly EmployeeService _emps;
    public ChangePasswordModel(IOptions<AppOptions> opt, EmployeeService emps) { _opt = opt.Value; _emps = emps; }

    public int MinLength => _opt.PasswordMinLength;
    public bool Forced => User.HasClaim(c => c.Type == Claims.MustChangePassword);
    [BindProperty] public string Current { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string Confirm { get; set; } = "";
    public string? Error { get; private set; }

    public IActionResult OnGet() => _opt.IsManual ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_opt.IsManual) return NotFound();
        if (NewPassword != Confirm) { Error = "The two new passwords do not match."; return Page(); }
        var me = User.Identity!.Name!;
        if (await _emps.VerifyLoginAsync(me, Current) is not { } emp) { Error = "Your current password is not correct."; return Page(); }
        Error = await _emps.ChangePasswordAsync(me, emp.Id, NewPassword, false);
        if (Error != null) return Page();
        return LocalRedirect("/");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return LocalRedirect(_opt.IsManual ? "/Account/Login" : "/");
    }
}
