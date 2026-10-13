using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;
using Tambola.Domain.Game;
using Tambola.Web.Auth;
using Tambola.Web.Data;

namespace Tambola.Web.Pages.Rooms;

public class ResultsModel : PageModel
{
    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly Services.GameService _game;
    public ResultsModel(IDbContextFactory<TambolaDb> dbf, Services.GameService game) { _dbf = dbf; _game = game; }

    /// <summary>The room's host (or an Admin) may delete a finished game.</summary>
    public bool CanDelete { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnPostDeleteAsync(string code)
    {
        try { await _game.DeleteRoomAsync(code, User.GetEmployeeId(), User.Identity!.Name!, User.IsInRole("Admin")); }
        catch (Services.GameException ex) { TempData["Err"] = ex.Message; return RedirectToPage(new { code }); }
        TempData["Msg"] = "The game was deleted. It is no longer visible to users.";
        return Redirect("/");
    }

    public Room Room { get; private set; } = null!;
    public string HostName { get; private set; } = "";
    public int[] Called { get; private set; } = Array.Empty<int>();
    public int PlayerCount { get; private set; }
    public record WinnerRow(int Slot, string Name, int AtCall, bool IsMe);
    public record PrizeRow(string Name, int Of, string? Reward, List<WinnerRow> Winners);
    public List<PrizeRow> Prizes { get; private set; } = new();
    public List<string> MyWins { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(string code)
    {
        var me = User.GetEmployeeId();
        await using var db = await _dbf.CreateDbContextAsync();
        code = (code ?? "").ToUpperInvariant();
        var room = await db.Rooms.AsNoTracking().Include(r => r.Host).Include(r => r.Prizes).ThenInclude(p => p.Winners).ThenInclude(w => w.Employee)
            .FirstOrDefaultAsync(r => r.Code == code);
        if (room == null) return NotFound();
        if (room.Status is RoomStatus.Lobby or RoomStatus.Running or RoomStatus.Paused)
            return Redirect(room.HostId == me ? "/Rooms/Host/" + code : "/Rooms/Play/" + code);
        Room = room; HostName = room.Host!.DisplayName;
        CanDelete = room.HostId == me || User.IsInRole("Admin");
        Error = TempData["Err"] as string;
        Called = await db.CalledNumbers.Where(c => c.RoomId == room.Id).OrderBy(c => c.Sequence).Select(c => c.Number).ToArrayAsync();
        PlayerCount = await db.RoomPlayers.CountAsync(p => p.RoomId == room.Id && !p.IsRemoved);
        Prizes = room.Prizes.OrderBy(p => p.Pattern).Select(p => new PrizeRow(PatternCatalog.Get(p.Pattern).DisplayName, p.WinnerCount, p.RewardText,
            p.Winners.OrderBy(w => w.Slot).Select(w => new WinnerRow(w.Slot, w.Employee!.DisplayName, w.AwardedAtSequence, w.EmployeeId == me)).ToList())).ToList();
        MyWins = Prizes.Where(p => p.Winners.Any(w => w.IsMe)).Select(p => $"{p.Name} (winner {p.Winners.First(w => w.IsMe).Slot} of {p.Of})" + (p.Reward != null ? " — " + p.Reward : "")).ToList();
        return Page();
    }
}
