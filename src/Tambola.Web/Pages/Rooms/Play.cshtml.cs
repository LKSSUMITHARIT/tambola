using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tambola.Domain.Entities;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Rooms;

/// <summary>Player screen (lobby + live game in one page, driven by SignalR). Hosts are redirected to the console.</summary>
public class PlayModel : PageModel
{
    private readonly GameService _game;
    public PlayModel(GameService game) => _game = game;
    public string Code { get; private set; } = "";
    public string RoomName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string code)
    {
        var room = await _game.FindRoomAsync(code);
        if (room == null) return NotFound();
        if (room.HostId == User.GetEmployeeId()) return Redirect("/Rooms/Host/" + room.Code);
        if (room.Status is RoomStatus.Completed or RoomStatus.Cancelled) return Redirect("/Rooms/Results/" + room.Code);
        Code = room.Code; RoomName = room.Name;
        return Page();
    }
}
