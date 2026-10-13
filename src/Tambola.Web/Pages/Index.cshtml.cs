using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;
using Tambola.Web.Auth;
using Tambola.Web.Data;

namespace Tambola.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly Services.GameService _game;
    public IndexModel(IDbContextFactory<TambolaDb> dbf, Services.GameService game) { _dbf = dbf; _game = game; }

    public record RoomRow(string Code, string Name, string Host, int Players, int MaxPlayers, RoomStatus Status, int Calls);
    public record PastRow(string Code, string Name, string Host, DateTime Date, RoomStatus Status, bool IAmHost);
    /// <summary>Live games: open to join.</summary>
    public List<RoomRow> Open { get; private set; } = new();
    /// <summary>Games I host that are still live (Lobby/Running/Paused).</summary>
    public List<RoomRow> Mine { get; private set; } = new();
    /// <summary>Finished games (Completed/Cancelled) that I hosted or played. Deleted games never appear.</summary>
    public List<PastRow> Past { get; private set; } = new();
    public string? Message { get; private set; }

    public async Task<IActionResult> OnPostDeleteAsync(string code)
    {
        try { await _game.DeleteRoomAsync(code, User.GetEmployeeId(), User.Identity!.Name!, User.IsInRole("Admin")); TempData["Msg"] = "The game was deleted."; }
        catch (Services.GameException ex) { TempData["Msg"] = ex.Message; }
        return RedirectToPage();
    }
    public int Games { get; private set; }
    public int PrizesWon { get; private set; }
    public int FullHouses { get; private set; }
    public string DisplayName { get; private set; } = "";
    [BindProperty] public string? Code { get; set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => await Load();

    public async Task<IActionResult> OnPostAsync()
    {
        var code = (Code ?? "").Trim().ToUpperInvariant();
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Code == code);
        if (room == null) { Error = "No room found with that code."; await Load(); return Page(); }
        return room.HostId == User.GetEmployeeId() ? Redirect("/Rooms/Host/" + code) : Redirect("/Rooms/Play/" + code);
    }

    private async Task Load()
    {
        var me = User.GetEmployeeId();
        DisplayName = User.GetDisplayName();
        await using var db = await _dbf.CreateDbContextAsync();
        IQueryable<RoomRow> Rows(IQueryable<Room> q) => q.Select(r => new RoomRow(r.Code, r.Name, r.Host!.DisplayName,
            r.Players.Count(p => !p.IsRemoved), r.MaxPlayers, r.Status, r.Calls.Count));
        Open = await Rows(db.Rooms.Where(r => (r.Status == RoomStatus.Lobby || r.Status == RoomStatus.Running || r.Status == RoomStatus.Paused) && !r.IsLocked)
            .OrderByDescending(r => r.CreatedAt).Take(20)).ToListAsync();
        Mine = await Rows(db.Rooms.Where(r => r.HostId == me && (r.Status == RoomStatus.Lobby || r.Status == RoomStatus.Running || r.Status == RoomStatus.Paused))
            .OrderByDescending(r => r.CreatedAt).Take(10)).ToListAsync();
        Past = (await db.Rooms.AsNoTracking().Include(r => r.Host)
            .Where(r => (r.Status == RoomStatus.Completed || r.Status == RoomStatus.Cancelled) &&
                        (r.HostId == me || r.Players.Any(p => p.EmployeeId == me)))
            .OrderByDescending(r => r.CreatedAt).Take(30).ToListAsync())
            .Select(r => new PastRow(r.Code, r.Name, r.Host!.DisplayName, (r.EndedAt ?? r.CreatedAt).ToLocalTime(), r.Status, r.HostId == me)).ToList();
        Message = TempData["Msg"] as string;
        Games = await db.RoomPlayers.CountAsync(p => p.EmployeeId == me);
        PrizesWon = await db.PrizeWinners.CountAsync(w => w.EmployeeId == me);
        FullHouses = await db.PrizeWinners.CountAsync(w => w.EmployeeId == me && db.Prizes.Any(p => p.Id == w.PrizeId && p.Pattern == PrizePattern.FullHouse));
    }
}
