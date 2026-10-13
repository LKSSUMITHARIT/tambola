using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Pages;

public class RegisterModel : PageModel
{
    private readonly EmployeeService _emps;
    private readonly AppOptions _opt;
    public RegisterModel(EmployeeService emps, Microsoft.Extensions.Options.IOptions<AppOptions> opt) { _emps = emps; _opt = opt.Value; }

    public string WindowsUser => User.Identity!.Name ?? "";
    [BindProperty] public string DisplayName { get; set; } = "";
    [BindProperty] public string EmployeeCode { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    public string? Error { get; private set; }
    public bool Deactivated { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_opt.IsManual) return Redirect(User.Identity?.IsAuthenticated == true ? "/" : "/Account/Register");   // Linux mode registers on /Account/Register
        var emp = await _emps.FindByWindowsUserAsync(WindowsUser);
        if (emp != null)
        {
            if (emp.IsActive) return Redirect("/");
            Deactivated = true;
        }
        DisplayName = EmployeeService.GuessDisplayName(WindowsUser);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (_opt.IsManual) return NotFound();
        Error = await _emps.RegisterAsync(WindowsUser, DisplayName, EmployeeCode, Email);
        if (Error != null) return Page();
        // Claims are added per request from the database, so a plain redirect is enough.
        return Redirect("/");
    }
}
