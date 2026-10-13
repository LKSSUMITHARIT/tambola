using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;
using Tambola.Domain.Game;
using Tambola.Web.Auth;
using Tambola.Web.Data;

namespace Tambola.Web.Pages;

public class HistoryModel : PageModel
{
    private readonly IDbContextFactory<TambolaDb> _dbf;
    public HistoryModel(IDbContextFactory<TambolaDb> dbf) => _dbf = dbf;

    public record Row(DateTime Date, string Code, string Room, string Host, int Tickets, List<string> Wins, RoomStatus Status);
    public List<Row> Rows { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var me = User.GetEmployeeId();
        await using var db = await _dbf.CreateDbContextAsync();
        var rooms = await db.Rooms.AsNoTracking().Include(r => r.Host)
            .Where(r => r.Players.Any(p => p.EmployeeId == me)).OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync();
        var ids = rooms.Select(r => r.Id).ToList();
        var tickets = await db.Tickets.Where(t => ids.Contains(t.RoomId) && t.EmployeeId == me).GroupBy(t => t.RoomId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var wins = await db.PrizeWinners.Where(w => w.EmployeeId == me).Join(db.Prizes, w => w.PrizeId, p => p.Id, (w, p) => new { p.RoomId, p.Pattern }).ToListAsync();
        Rows = rooms.Select(r => new Row(r.CreatedAt.ToLocalTime(), r.Code, r.Name, r.Host!.DisplayName, tickets.GetValueOrDefault(r.Id),
            wins.Where(w => w.RoomId == r.Id).Select(w => PatternCatalog.Get(w.Pattern).DisplayName).ToList(), r.Status)).ToList();
    }
}
