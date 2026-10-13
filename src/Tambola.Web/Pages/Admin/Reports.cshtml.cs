using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;
using Tambola.Web.Data;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Admin;

public class ReportsModel : PageModel
{
    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly GameService _game;
    public ReportsModel(IDbContextFactory<TambolaDb> dbf, GameService game) { _dbf = dbf; _game = game; }

    public record DeletedRow(string Code, string Name, string Host, RoomStatus Status, DateTime? DeletedAt, string? DeletedBy);
    public List<DeletedRow> Deleted { get; private set; } = new();
    public int Games { get; private set; }
    public int Players { get; private set; }
    public int Prizes { get; private set; }
    public record LeaderRow(string Name, string Code, int Games, int Wins);
    public List<LeaderRow> Leaders { get; private set; } = new();
    public List<AuditLog> Audit { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        // reports include games the hosts deleted (they are only hidden from users)
        Games = await db.Rooms.IgnoreQueryFilters().CountAsync(r => r.Status == RoomStatus.Completed);
        Deleted = (await db.Rooms.IgnoreQueryFilters().AsNoTracking().Include(r => r.Host).Where(r => r.IsDeleted)
            .OrderByDescending(r => r.DeletedAt).Take(100).ToListAsync())
            .Select(r => new DeletedRow(r.Code, r.Name, r.Host!.DisplayName, r.Status, r.DeletedAt?.ToLocalTime(), r.DeletedBy)).ToList();
        Players = await db.RoomPlayers.Select(p => p.EmployeeId).Distinct().CountAsync();
        Prizes = await db.PrizeWinners.CountAsync();
        Leaders = await BuildLeaders(db);
        Audit = await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Take(50).ToListAsync();
    }

    private static async Task<List<LeaderRow>> BuildLeaders(TambolaDb db) =>
        (await db.Employees.AsNoTracking().Select(e => new LeaderRow(e.DisplayName, e.EmployeeCode,
            db.RoomPlayers.Count(p => p.EmployeeId == e.Id), db.PrizeWinners.Count(w => w.EmployeeId == e.Id))).ToListAsync())
        .Where(l => l.Games > 0).OrderByDescending(l => l.Wins).ThenByDescending(l => l.Games).Take(20).ToList();

    public async Task<IActionResult> OnPostRestoreAsync(string code)
    {
        await _game.RestoreRoomAsync(code, User.Identity!.Name!);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var sb = new StringBuilder("Employee,Code,Games,Wins\r\n");
        foreach (var l in await BuildLeaders(db)) sb.Append($"\"{l.Name.Replace("\"", "\"\"")}\",{l.Code},{l.Games},{l.Wins}\r\n");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "tambola-leaderboard.csv");
    }
}
