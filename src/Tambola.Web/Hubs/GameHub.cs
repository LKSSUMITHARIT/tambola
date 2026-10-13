using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Tambola.Domain.Entities;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Hubs;

/// <summary>Thin SignalR hub: authorise → call GameService → (service broadcasts).</summary>
[Authorize(Policy = Policies.Registered)]
public class GameHub : Hub
{
    private readonly GameService _game;
    public GameHub(GameService game) => _game = game;

    private int Me => Context.User!.GetEmployeeId();

    private static async Task<T> Guard<T>(Func<Task<T>> f)
    {
        try { return await f(); }
        catch (GameException ex) { throw new HubException(ex.Message); }
    }

    // ── player + host ──
    public Task<RoomStateDto> JoinRoom(string code) => Guard(async () =>
    {
        await _game.JoinAsync(code, Me);
        var state = await _game.GetStateAsync(code, Me);
        await Groups.AddToGroupAsync(Context.ConnectionId, state.code);
        return state;
    });

    public Task<RoomStateDto> GetState(string code) => Guard(() => _game.GetStateAsync(code, Me));
    public Task<TicketDto> BuyTicket(string code) => Guard(() => _game.BuyTicketAsync(code, Me));
    public Task<int[]> Mark(string code, int ticketId, int number) => Guard(() => _game.MarkAsync(code, Me, ticketId, number));

    public Task<ClaimOutcome> Claim(string code, int ticketId, string pattern) => Guard(() =>
    {
        if (!Enum.TryParse<PrizePattern>(pattern, out var p)) throw new GameException("Unknown prize.");
        return _game.ClaimAsync(code, Me, ticketId, p);
    });

    // ── host only (the service re-checks that the caller is this room's host) ──
    [Authorize(Policy = Policies.Host)]
    public Task<bool> Start(string code) => Guard(async () => { await _game.StartAsync(code, Me); return true; });
    [Authorize(Policy = Policies.Host)]
    public Task<bool> CallNext(string code) => Guard(async () => { await _game.CallNextAsync(code, Me); return true; });
    [Authorize(Policy = Policies.Host)]
    public Task<bool> SetPaused(string code, bool paused) => Guard(async () => { await _game.SetPausedAsync(code, Me, paused); return true; });
    [Authorize(Policy = Policies.Host)]
    public Task<bool> SetCalling(string code, string mode, int interval) => Guard(async () =>
    {
        await _game.SetCallingAsync(code, Me, mode == "Auto" ? CallMode.Auto : CallMode.Manual, interval); return true;
    });
    [Authorize(Policy = Policies.Host)]
    public Task<bool> End(string code, bool cancel) => Guard(async () => { await _game.EndAsync(code, Me, cancel); return true; });
    [Authorize(Policy = Policies.Host)]
    public Task<bool> SetLocked(string code, bool locked) => Guard(async () => { await _game.SetLockedAsync(code, Me, locked); return true; });
    [Authorize(Policy = Policies.Host)]
    public Task<bool> RemovePlayer(string code, int employeeId) => Guard(async () => { await _game.RemovePlayerAsync(code, Me, employeeId); return true; });
}
