using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;
using Tambola.Web.Data;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly EmployeeService _emps;
    public IndexModel(IDbContextFactory<TambolaDb> dbf, EmployeeService emps) { _dbf = dbf; _emps = emps; }

    public List<Employee> Employees { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public async Task OnGetAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var q = db.Employees.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var s = Q.Trim().ToLower();
            q = q.Where(e => e.DisplayName.ToLower().Contains(s) || e.EmployeeCode.ToLower().Contains(s) || e.Email.Contains(s) || e.WindowsUser.ToLower().Contains(s));
        }
        Employees = await q.OrderBy(e => e.DisplayName).ToListAsync();
    }

    public async Task<IActionResult> OnPostHostAsync(int id, bool on)
    {
        await _emps.SetHostAsync(User.Identity!.Name!, id, on);
        return RedirectToPage(new { Q });
    }

    [TempData] public string? TempPasswordInfo { get; set; }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id)
    {
        var temp = await _emps.ResetPasswordAsync(User.Identity!.Name!, id);
        TempPasswordInfo = temp;
        return RedirectToPage(new { Q });
    }

    public async Task<IActionResult> OnPostActiveAsync(int id, bool on)
    {
        await _emps.SetActiveAsync(User.Identity!.Name!, id, on);
        return RedirectToPage(new { Q });
    }
}
