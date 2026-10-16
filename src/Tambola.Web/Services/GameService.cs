using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tambola.Domain.Entities;
using Tambola.Domain.Game;
using Tambola.Web.Data;
using Tambola.Web.Hubs;

namespace Tambola.Web.Services;

/// <summary>Expected, user-facing error (shown as a message to the caller).</summary>
public class GameException : Exception { public GameException(string m) : base(m) { } }

public record PrizeDto(string pattern, string name, int winnerCount, string? reward, List<string> winners, bool closed);
public record TicketDto(int id, int serial, int[] grid, int[] marks);
public record RoomStateDto(string code, string name, string host, int myId, string status, string callMode, string cutMode,
    int interval, int maxTickets, bool isHost, bool locked, int lateJoinMaxCalls, int[] called, List<PrizeDto> prizes,
    List<TicketDto> tickets, List<PlayerDto> players, Dictionary<string, string> mine,
    int maxWrongClaims = 3, Dictionary<string, int>? wrong = null, Dictionary<string, int[]>? wonTickets = null);
public record PlayerDto(int id, string name);
/// <summary>status: won | late | invalid (wrong claim) | rejected. For "invalid": locked = the prize is now locked for this player; left = tries remaining (-1 = unlimited).</summary>
public record ClaimOutcome(string status, string message, string? prize, int slot, int of, string? reward, bool locked = false, int left = 0);

/// <summary>MaxWrongClaims: wrong claims allowed per prize per player before it locks (0 = unlimited).</summary>
public record CreateRoomRequest(string Name, int MaxPlayers, int MaxTickets, CallMode CallMode, int IntervalSeconds,
    CutMode CutMode, List<PrizeRequest> Prizes, int MaxWrongClaims = 3);
public record PrizeRequest(PrizePattern Pattern, int Winners, string? Reward);

/// <summary>All game rules. The server is authoritative: clients never get uncalled numbers and claims are re-validated here.</summary>
public class GameService
{
    /// <summary>Largest allowed room (players, excluding the host).</summary>
    public const int MaxPlayersLimit = 1500;

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    /// <summary>Called numbers per room in call order. Only changed while holding the room lock, so it never lags the database.</summary>
    private readonly ConcurrentDictionary<int, int[]> CalledCache = new();   // per app instance (GameService is a singleton)
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly IHubContext<GameHub> _hub;
    private readonly AppOptions _opt;
    private readonly ILogger<GameService> _log;

    public GameService(IDbContextFactory<TambolaDb> dbf, IHubContext<GameHub> hub, IOptions<AppOptions> opt, ILogger<GameService> log)
    {
        _dbf = dbf; _hub = hub; _opt = opt.Value; _log = log;
    }

    private static SemaphoreSlim LockFor(int roomId) => Locks.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));

    // ───────────────────────── rooms ─────────────────────────

    public async Task<string> CreateRoomAsync(int hostId, CreateRoomRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new GameException("Room name is required.");
        var prizes = r.Prizes.Where(p => p.Winners > 0).GroupBy(p => p.Pattern).Select(g => g.First()).ToList();
        if (prizes.Count == 0) throw new GameException("Enable at least one prize.");
        await using var db = await _dbf.CreateDbContextAsync();

        string code;
        do { code = new string(Enumerable.Range(0, 6).Select(_ => CodeAlphabet[TicketGenerator.NextInt(CodeAlphabet.Length)]).ToArray()); }
        while (await db.Rooms.AnyAsync(x => x.Code == code));

        var room = new Room
        {
            Code = code, Name = r.Name.Trim(), HostId = hostId,
            MaxPlayers = Math.Clamp(r.MaxPlayers, 1, MaxPlayersLimit),
            MaxWrongClaims = Math.Clamp(r.MaxWrongClaims, 0, 1000),
            MaxTicketsPerPlayer = Math.Clamp(r.MaxTickets, 1, 6),
            CallMode = r.CallMode, CutMode = r.CutMode,
            CallIntervalSeconds = Math.Clamp(r.IntervalSeconds, 3, 15),
            DrawOrder = string.Join(',', TicketGenerator.NewDrawOrder()),
            Prizes = prizes.Select(p => new Prize { Pattern = p.Pattern, WinnerCount = Math.Clamp(p.Winners, 1, 100), RewardText = string.IsNullOrWhiteSpace(p.Reward) ? null : p.Reward.Trim() }).ToList()
        };
        db.Rooms.Add(room);
        var host = await db.Employees.FindAsync(hostId);
        db.AuditLogs.Add(new AuditLog { Actor = host?.WindowsUser ?? "?", Action = "CreateRoom", Details = $"{code} {room.Name}" });
        await db.SaveChangesAsync();
        return code;
    }

    private static async Task<Room> LoadRoom(TambolaDb db, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        return await db.Rooms.Include(r => r.Prizes).ThenInclude(p => p.Winners)
                   .FirstOrDefaultAsync(r => r.Code == code) ?? throw new GameException("Room not found.");
    }

    /// <summary>Room without prizes/winners: enough for join and ticket checks, and much cheaper in big rooms.</summary>
    private static async Task<Room> LoadRoomLite(TambolaDb db, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        return await db.Rooms.FirstOrDefaultAsync(r => r.Code == code) ?? throw new GameException("Room not found.");
    }

    private async Task<int[]> CalledOf(TambolaDb db, int roomId)
    {
        if (CalledCache.TryGetValue(roomId, out var c)) return c;
        var loaded = await db.CalledNumbers.Where(x => x.RoomId == roomId).OrderBy(x => x.Sequence).Select(x => x.Number).ToArrayAsync();
        return CalledCache.GetOrAdd(roomId, loaded);
    }

    public async Task<Room?> FindRoomAsync(string code)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        code = (code ?? "").Trim().ToUpperInvariant();
        return await db.Rooms.AsNoTracking().Include(r => r.Host).FirstOrDefaultAsync(r => r.Code == code);
    }

    // ───────────────────────── join / tickets ─────────────────────────

    public async Task JoinAsync(string code, int empId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoomLite(db, code);
        if (room.HostId == empId) return;
        var rp = await db.RoomPlayers.FirstOrDefaultAsync(p => p.RoomId == room.Id && p.EmployeeId == empId);
        if (rp != null)
        {
            if (rp.IsRemoved) throw new GameException("You were removed from this room by the host.");
            return;
        }
        if (room.Status is RoomStatus.Completed or RoomStatus.Cancelled) throw new GameException("This game has ended.");
        if (room.IsLocked) throw new GameException("The host has locked this room.");
        if (await db.RoomPlayers.CountAsync(p => p.RoomId == room.Id && !p.IsRemoved) >= room.MaxPlayers) throw new GameException("This room is full.");
        if (room.Status != RoomStatus.Lobby && (await CalledOf(db, room.Id)).Length > _opt.LateJoinMaxCalls)
            throw new GameException("This game has progressed too far to join.");
        db.RoomPlayers.Add(new RoomPlayer { RoomId = room.Id, EmployeeId = empId });
        try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return; /* double click race: already joined */ }
        // Send only the new player. A full player list per join is O(players²) data and was the main bottleneck at 700+ players.
        var name = await db.Employees.AsNoTracking().Where(e => e.Id == empId).Select(e => e.DisplayName).FirstAsync();
        await _hub.Clients.Group(room.Code).SendAsync("PlayerJoined", new PlayerDto(empId, name));
    }

    public async Task<TicketDto> BuyTicketAsync(string code, int empId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoomLite(db, code);
        var sem = LockFor(room.Id); await sem.WaitAsync();
        try
        {
            await db.Entry(room).ReloadAsync();
            if (room.HostId == empId) throw new GameException("Hosts cannot hold tickets in their own room.");
            var rp = await db.RoomPlayers.FirstOrDefaultAsync(p => p.RoomId == room.Id && p.EmployeeId == empId && !p.IsRemoved)
                     ?? throw new GameException("Join the room first.");
            var calls = (await CalledOf(db, room.Id)).Length;
            if (room.Status == RoomStatus.Paused || room.Status is RoomStatus.Completed or RoomStatus.Cancelled) throw new GameException("Tickets can't be taken now.");
            if (room.Status == RoomStatus.Running && calls > _opt.LateJoinMaxCalls) throw new GameException("Too late to take a ticket in this game.");
            var mine = await db.Tickets.CountAsync(t => t.RoomId == room.Id && t.EmployeeId == empId);
            if (mine >= room.MaxTicketsPerPlayer) throw new GameException($"You can hold at most {room.MaxTicketsPerPlayer} tickets.");

            // Uniqueness is enforced by the unique (RoomId, Grid) index: a duplicate grid fails the insert and we simply draw again,
            // instead of loading every ticket of the room on each purchase.
            var serial = await db.Tickets.CountAsync(t => t.RoomId == room.Id) + 1;
            for (var attempt = 0; ; attempt++)
            {
                var t = new Ticket { RoomId = room.Id, EmployeeId = empId, Grid = TicketGenerator.Generate().Serialize(), Serial = serial };
                db.Tickets.Add(t);
                try { await db.SaveChangesAsync(); return ToDto(t); }
                catch (DbUpdateException) when (attempt < 50) { db.Entry(t).State = EntityState.Detached; }
            }
        }
        finally { sem.Release(); }
    }

    public async Task<int[]> MarkAsync(string code, int empId, int ticketId, int number)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoomLite(db, code);
        if (room.CutMode == CutMode.Auto) throw new GameException("Numbers are cut automatically in this room.");
        var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId && x.RoomId == room.Id && x.EmployeeId == empId)
                ?? throw new GameException("Ticket not found.");
        if (!TicketGrid.Parse(t.Grid).Numbers().Contains(number)) throw new GameException("That number is not on your ticket.");
        if (!(await CalledOf(db, room.Id)).Contains(number)) throw new GameException($"{number} has not been called yet.");
        var marks = ParseMarks(t.Marks);
        if (!marks.Remove(number)) marks.Add(number);
        t.Marks = string.Join(',', marks);
        await db.SaveChangesAsync();
        return marks.ToArray();
    }

    // ───────────────────────── host controls ─────────────────────────

    private async Task<(TambolaDb db, Room room)> HostRoom(string code, int hostId)
    {
        var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoom(db, code);
        if (room.HostId != hostId) { await db.DisposeAsync(); throw new GameException("Only the host can do this."); }
        return (db, room);
    }

    public async Task StartAsync(string code, int hostId)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        var sem = LockFor(room.Id); await sem.WaitAsync();
        try
        {
            if (room.Status != RoomStatus.Lobby) throw new GameException("The game has already started.");
            if (!await db.Tickets.AnyAsync(t => t.RoomId == room.Id)) throw new GameException("No player has taken a ticket yet.");
            room.Status = RoomStatus.Running; room.StartedAt = DateTime.UtcNow;
            room.NextCallAt = room.CallMode == CallMode.Auto ? DateTime.UtcNow.AddSeconds(2) : null;
            await db.SaveChangesAsync();
        }
        finally { sem.Release(); }
        await BroadcastStatus(room);
    }

    public async Task CallNextAsync(string code, int hostId)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        if (room.CallMode == CallMode.Auto) throw new GameException("Calling is automatic in this room. Switch to manual first.");
        await CallLocked(db, room);
    }

    /// <summary>Used by the auto caller.</summary>
    public async Task AutoCallAsync(int roomId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await db.Rooms.Include(r => r.Prizes).ThenInclude(p => p.Winners).FirstOrDefaultAsync(r => r.Id == roomId);
        if (room == null) return;
        try { await CallLocked(db, room); } catch (GameException) { }
    }

    private async Task CallLocked(TambolaDb db, Room room)
    {
        var sem = LockFor(room.Id); await sem.WaitAsync();
        int seq, number;
        try
        {
            await db.Entry(room).ReloadAsync();
            if (room.Status != RoomStatus.Running) throw new GameException("The game is not running.");
            var calledSoFar = await CalledOf(db, room.Id);
            var count = calledSoFar.Length;
            if (count >= 90) { room.NextCallAt = null; await db.SaveChangesAsync(); throw new GameException("All 90 numbers have been called."); }
            number = int.Parse(room.DrawOrder.Split(',')[count]);
            seq = count + 1;
            db.CalledNumbers.Add(new CalledNumber { RoomId = room.Id, Sequence = seq, Number = number });
            room.NextCallAt = room.CallMode == CallMode.Auto && seq < 90 ? DateTime.UtcNow.AddSeconds(room.CallIntervalSeconds) : null;
            await db.SaveChangesAsync();
            CalledCache[room.Id] = calledSoFar.Append(number).ToArray();   // only after the row is saved
        }
        finally { sem.Release(); }
        await _hub.Clients.Group(room.Code).SendAsync("NumberCalled", new { sequence = seq, number });
    }

    public async Task SetPausedAsync(string code, int hostId, bool paused)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        var sem = LockFor(room.Id); await sem.WaitAsync();
        try
        {
            if (paused && room.Status != RoomStatus.Running) throw new GameException("The game is not running.");
            if (!paused && room.Status != RoomStatus.Paused) throw new GameException("The game is not paused.");
            room.Status = paused ? RoomStatus.Paused : RoomStatus.Running;
            room.NextCallAt = !paused && room.CallMode == CallMode.Auto ? DateTime.UtcNow.AddSeconds(room.CallIntervalSeconds) : null;
            await db.SaveChangesAsync();
        }
        finally { sem.Release(); }
        await BroadcastStatus(room);
    }

    public async Task SetCallingAsync(string code, int hostId, CallMode mode, int interval)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        if (room.Status is RoomStatus.Completed or RoomStatus.Cancelled) throw new GameException("The game has ended.");
        room.CallMode = mode; room.CallIntervalSeconds = Math.Clamp(interval, 3, 15);
        room.NextCallAt = mode == CallMode.Auto && room.Status == RoomStatus.Running ? DateTime.UtcNow.AddSeconds(room.CallIntervalSeconds) : null;
        await db.SaveChangesAsync();
        await BroadcastStatus(room);
    }

    public async Task EndAsync(string code, int hostId, bool cancel)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        if (room.Status is RoomStatus.Completed or RoomStatus.Cancelled) return;
        room.Status = cancel || room.Status == RoomStatus.Lobby ? RoomStatus.Cancelled : RoomStatus.Completed;
        room.EndedAt = DateTime.UtcNow; room.NextCallAt = null;
        await db.SaveChangesAsync();
        await BroadcastStatus(room);
    }

    /// <summary>
    /// Soft delete: the room disappears for everyone (global query filter) but stays in the database for Admin reports.
    /// Allowed for the room's host or an Admin, and only once the game has ended.
    /// </summary>
    public async Task DeleteRoomAsync(string code, int actorId, string actorName, bool isAdmin)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoomLite(db, code);
        if (room.HostId != actorId && !isAdmin) throw new GameException("Only the host can delete this game.");
        if (room.Status is not (RoomStatus.Completed or RoomStatus.Cancelled))
            throw new GameException("Only a finished game can be deleted. End or cancel it first.");
        room.IsDeleted = true; room.DeletedAt = DateTime.UtcNow; room.DeletedBy = actorName;
        db.AuditLogs.Add(new AuditLog { Actor = actorName, Action = "DeleteRoom", Details = $"{room.Code} {room.Name}" });
        await db.SaveChangesAsync();
        CalledCache.TryRemove(room.Id, out _);
    }

    /// <summary>Admin only (caller checks): bring a deleted game back.</summary>
    public async Task RestoreRoomAsync(string code, string actorName)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var c = (code ?? "").Trim().ToUpperInvariant();
        var room = await db.Rooms.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Code == c) ?? throw new GameException("Room not found.");
        room.IsDeleted = false; room.DeletedAt = null; room.DeletedBy = null;
        db.AuditLogs.Add(new AuditLog { Actor = actorName, Action = "RestoreRoom", Details = $"{room.Code} {room.Name}" });
        await db.SaveChangesAsync();
    }

    public async Task SetLockedAsync(string code, int hostId, bool locked)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        room.IsLocked = locked;
        await db.SaveChangesAsync();
        await BroadcastStatus(room);
    }

    public async Task RemovePlayerAsync(string code, int hostId, int empId)
    {
        var (db, room) = await HostRoom(code, hostId);
        await using var _ = db;
        var rp = await db.RoomPlayers.FirstOrDefaultAsync(p => p.RoomId == room.Id && p.EmployeeId == empId) ?? throw new GameException("Player not found.");
        rp.IsRemoved = true;
        await db.SaveChangesAsync();
        await _hub.Clients.Group(room.Code).SendAsync("PlayerRemoved", new { id = empId });
        await BroadcastPlayers(db, room);
    }

    // ───────────────────────── claims ─────────────────────────

    public async Task<ClaimOutcome> ClaimAsync(string code, int empId, int ticketId, PrizePattern pattern)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoomLite(db, code);
        var sem = LockFor(room.Id); await sem.WaitAsync();
        ClaimOutcome outcome; string? feed = null; var closedAll = false;
        try
        {
            await db.Entry(room).ReloadAsync();
            if (room.Status != RoomStatus.Running) return new ClaimOutcome("rejected", "The game is not running.", null, 0, 0, null);
            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.RoomId == room.Id && t.EmployeeId == empId);
            if (ticket == null) return new ClaimOutcome("rejected", "That ticket is not yours.", null, 0, 0, null);
            var prize = await db.Prizes.Include(p => p.Winners).FirstOrDefaultAsync(p => p.RoomId == room.Id && p.Pattern == pattern);
            if (prize == null) return new ClaimOutcome("rejected", "That prize is not part of this game.", null, 0, 0, null);
            var def = PatternCatalog.Get(pattern);
            var limit = room.MaxWrongClaims;   // 0 = unlimited
            var earlier = await db.Claims.Where(c => c.PrizeId == prize.Id && c.EmployeeId == empId && c.Result != ClaimResult.Late)
                .Select(c => c.Result).ToListAsync();
            // one win per ticket: a player holding several tickets may win the same prize once with each of them
            if (prize.Winners.Any(w => w.TicketId == ticketId))
                return new ClaimOutcome("rejected", "This ticket has already won this prize. Claim with another ticket.", def.DisplayName, 0, 0, null);
            var wrongSoFar = earlier.Count(r => r == ClaimResult.Bogey);
            if (limit > 0 && wrongSoFar >= limit)
                return new ClaimOutcome("rejected", $"This prize is locked for you after {limit} wrong claim{(limit == 1 ? "" : "s")}.", def.DisplayName, 0, 0, null, true, 0);

            var calledArr = await CalledOf(db, room.Id);
            var seq = calledArr.Length;
            ISet<int> called = calledArr.ToHashSet();
            var emp = await db.Employees.FindAsync(empId);

            if (!def.IsSatisfied(TicketGrid.Parse(ticket.Grid), called))
            {
                db.Claims.Add(new Claim { RoomId = room.Id, PrizeId = prize.Id, EmployeeId = empId, TicketId = ticketId, Result = ClaimResult.Bogey, AtSequence = seq, Reason = "Pattern not complete" });
                await db.SaveChangesAsync();
                var locked = limit > 0 && wrongSoFar + 1 >= limit;
                var left = limit == 0 ? -1 : Math.Max(0, limit - (wrongSoFar + 1));
                var tail = locked ? "This prize is now locked for you."
                         : limit == 0 ? "Check your ticket and try again."
                         : $"You have {left} wrong claim{(left == 1 ? "" : "s")} left for this prize.";
                outcome = new ClaimOutcome("invalid", $"Your ticket does not have all numbers for {def.DisplayName} called yet. {tail}", def.DisplayName, 0, prize.WinnerCount, null, locked, left);
            }
            else if (prize.Winners.Count >= prize.WinnerCount)
            {
                db.Claims.Add(new Claim { RoomId = room.Id, PrizeId = prize.Id, EmployeeId = empId, TicketId = ticketId, Result = ClaimResult.Late, AtSequence = seq, Reason = "Prize closed" });
                await db.SaveChangesAsync();
                outcome = new ClaimOutcome("late", $"Your claim for {def.DisplayName} was correct, but other players got there first. Be quicker next time!", def.DisplayName, 0, prize.WinnerCount, null);
            }
            else
            {
                var slot = prize.Winners.Count + 1;
                db.PrizeWinners.Add(new PrizeWinner { PrizeId = prize.Id, Slot = slot, EmployeeId = empId, TicketId = ticketId, AwardedAtSequence = seq });
                db.Claims.Add(new Claim { RoomId = room.Id, PrizeId = prize.Id, EmployeeId = empId, TicketId = ticketId, Result = ClaimResult.Valid, AtSequence = seq });
                await db.SaveChangesAsync();
                outcome = new ClaimOutcome("won", $"Congratulations! You won {def.DisplayName} (winner {slot} of {prize.WinnerCount}).", def.DisplayName, slot, prize.WinnerCount, prize.RewardText);
                feed = $"🏆 {emp?.DisplayName} won {def.DisplayName} (#{slot} of {prize.WinnerCount})";

                var all = await db.Prizes.Where(p => p.RoomId == room.Id).Select(p => new { p.WinnerCount, W = p.Winners.Count }).ToListAsync();
                if (all.All(p => p.W >= p.WinnerCount))
                {
                    room.Status = RoomStatus.Completed; room.EndedAt = DateTime.UtcNow; room.NextCallAt = null;
                    await db.SaveChangesAsync(); closedAll = true;
                }
            }
        }
        finally { sem.Release(); }

        if (feed != null)
        {
            await _hub.Clients.Group(room.Code).SendAsync("PrizesChanged", await BuildPrizes(db, room.Id));
            await _hub.Clients.Group(room.Code).SendAsync("Feed", feed);
        }
        if (closedAll) await BroadcastStatus(room);
        return outcome;
    }

    // ───────────────────────── snapshot / broadcast ─────────────────────────

    public async Task<RoomStateDto> GetStateAsync(string code, int empId)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var room = await LoadRoom(db, code);
        await db.Entry(room).Reference(r => r.Host).LoadAsync();
        var isHost = room.HostId == empId;
        if (!isHost && !await db.RoomPlayers.AnyAsync(p => p.RoomId == room.Id && p.EmployeeId == empId && !p.IsRemoved))
            throw new GameException("Join the room first.");

        var called = await CalledOf(db, room.Id);
        var tickets = isHost ? new List<Ticket>() : await db.Tickets.Where(t => t.RoomId == room.Id && t.EmployeeId == empId).OrderBy(t => t.Serial).ToListAsync();
        var mine = new Dictionary<string, string>();
        var myClaims = await db.Claims.Where(c => c.RoomId == room.Id && c.EmployeeId == empId && c.Result != ClaimResult.Late)
                         .Select(c => new { c.PrizeId, c.Result }).ToListAsync();
        var wrong = new Dictionary<string, int>();
        // tickets of this player that already won each prize (one win per ticket); "won" = every ticket has won it
        var wonTickets = room.Prizes.ToDictionary(p => p.Pattern.ToString(),
            p => p.Winners.Where(w => w.EmployeeId == empId).Select(w => w.TicketId).ToArray());
        foreach (var g in myClaims.GroupBy(c => c.PrizeId))
        {
            var pattern = room.Prizes.First(x => x.Id == g.Key).Pattern.ToString();
            var bogeys = g.Count(c => c.Result == ClaimResult.Bogey);
            if (tickets.Count > 0 && wonTickets[pattern].Length >= tickets.Count) mine[pattern] = "won";
            else if (room.MaxWrongClaims > 0 && bogeys >= room.MaxWrongClaims) mine[pattern] = "barred";   // limit reached: locked
            if (bogeys > 0) wrong[pattern] = bogeys;
        }
        return new RoomStateDto(room.Code, room.Name, room.Host!.DisplayName, empId, room.Status.ToString(), room.CallMode.ToString(),
            room.CutMode.ToString(), room.CallIntervalSeconds, room.MaxTicketsPerPlayer, isHost, room.IsLocked, _opt.LateJoinMaxCalls,
            called, await BuildPrizes(db, room.Id), tickets.Select(ToDto).ToList(), await PlayerList(db, room.Id), mine,
            room.MaxWrongClaims, wrong, wonTickets.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static TicketDto ToDto(Ticket t) =>
        new(t.Id, t.Serial, t.Grid.Split(',').Select(int.Parse).ToArray(), ParseMarks(t.Marks).ToArray());

    private static List<int> ParseMarks(string s) =>
        string.IsNullOrEmpty(s) ? new List<int>() : s.Split(',').Select(int.Parse).ToList();

    private static async Task<List<PrizeDto>> BuildPrizes(TambolaDb db, int roomId)
    {
        var prizes = await db.Prizes.AsNoTracking().Where(p => p.RoomId == roomId).Include(p => p.Winners).ThenInclude(w => w.Employee).OrderBy(p => p.Pattern).ToListAsync();
        return prizes.Select(p => new PrizeDto(p.Pattern.ToString(), PatternCatalog.Get(p.Pattern).DisplayName, p.WinnerCount, p.RewardText,
            p.Winners.OrderBy(w => w.Slot).Select(w => w.Employee!.DisplayName).ToList(), p.Winners.Count >= p.WinnerCount)).ToList();
    }

    private static async Task<List<PlayerDto>> PlayerList(TambolaDb db, int roomId) =>
        await db.RoomPlayers.AsNoTracking().Where(p => p.RoomId == roomId && !p.IsRemoved).OrderBy(p => p.JoinedAt)
            .Select(p => new PlayerDto(p.EmployeeId, p.Employee!.DisplayName)).ToListAsync();

    private async Task BroadcastPlayers(TambolaDb db, Room room) =>
        await _hub.Clients.Group(room.Code).SendAsync("PlayersChanged", await PlayerList(db, room.Id));

    private Task BroadcastStatus(Room room) =>
        _hub.Clients.Group(room.Code).SendAsync("StatusChanged", new { status = room.Status.ToString(), callMode = room.CallMode.ToString(), interval = room.CallIntervalSeconds, locked = room.IsLocked });

    // ───────────────────────── auto caller support ─────────────────────────

    public async Task<List<int>> DueAutoRoomsAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        return await db.Rooms.Where(r => r.Status == RoomStatus.Running && r.CallMode == CallMode.Auto && r.NextCallAt != null && r.NextCallAt <= now)
            .Select(r => r.Id).ToListAsync();
    }
}
