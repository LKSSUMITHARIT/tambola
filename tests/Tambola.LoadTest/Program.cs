// Tambola load test: launches the real (published, Release) server as a separate process on Kestrel,
// seeds users/rooms straight into its SQLite file, then drives it with real SignalR WebSocket clients.
//   dotnet run -c Release --project tests/Tambola.LoadTest -- --server <dir with Tambola.Web.dll> --out <dir> [--only A1,B2]
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tambola.Domain.Entities;
using Tambola.Web.Data;
using Tambola.Web.Services;

var arg = (string n, string d) => { var i = Array.IndexOf(args, "--" + n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : d; };
var serverDir = arg("server", "");
var outDir = arg("out", ".");
var only = arg("only", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
var callGapMs = int.Parse(arg("gap", "200"));
Directory.CreateDirectory(outDir);

// id, rooms, players per room
var scenarios = new (string Id, int Rooms, int Players)[]
{
    ("A1", 1, 50), ("A2", 1, 100), ("A3", 1, 200), ("A4", 1, 400), ("A5", 1, 700), ("A6", 1, 1000), ("A7", 1, 1500), ("C1", 2, 1000), ("C2", 3, 1000), ("C3", 2, 1500), ("C4", 3, 1500),
    ("B1", 10, 20), ("B2", 10, 50), ("B3", 10, 100), ("B4", 20, 100), ("B5", 40, 100),
}.Where(s => only.Count == 0 || only.Contains(s.Id)).ToArray();

var results = new List<Result>();
foreach (var s in scenarios)
{
    Console.WriteLine($"\n=== {s.Id}: {s.Rooms} room(s) x {s.Players} players ===");
    Result r;
    try { r = await Run(s.Id, s.Rooms, s.Players); }
    catch (Exception ex) { Console.WriteLine("SCENARIO CRASHED: " + ex); r = new Result { Id = s.Id, Rooms = s.Rooms, PlayersPerRoom = s.Players, Notes = "crashed: " + ex.Message }; }
    results.Add(r);
    Console.WriteLine(JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(outDir, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}
return 0;

async Task<Result> Run(string id, int rooms, int players)
{
    var port = 5099;
    var db = Path.Combine(Path.GetTempPath(), $"tambola-load-{id}-{Guid.NewGuid():N}.db");
    var cs = $"Data Source={db};Default Timeout=60";
    var res = new Result { Id = id, Rooms = rooms, PlayersPerRoom = players };

    // ── start the real server ──
    var psi = new ProcessStartInfo("dotnet", $"\"{Path.Combine(serverDir, "Tambola.Web.dll")}\"")
    {
        WorkingDirectory = serverDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
    };
    psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
    psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    psi.Environment["Tambola__AuthMode"] = "Dev";
    psi.Environment["ConnectionStrings__Tambola"] = cs;
    psi.Environment["Serilog__MinimumLevel__Default"] = "Warning";
    using var server = Process.Start(psi)!;
    var serverErr = new System.Text.StringBuilder();
    server.OutputDataReceived += (_, e) => { if (e.Data != null && e.Data.Contains("rror")) lock (serverErr) serverErr.AppendLine(e.Data); };
    server.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (serverErr) serverErr.AppendLine(e.Data); };
    server.BeginOutputReadLine(); server.BeginErrorReadLine();
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    for (var i = 0; ; i++)
    {
        try { if ((await http.GetAsync("/health")).IsSuccessStatusCode) break; } catch { }
        if (i > 100 || server.HasExited) throw new Exception("server did not start: " + serverErr);
        await Task.Delay(300);
    }

    try
    {
        // ── seed users + rooms (direct to the same SQLite file; server state lives in the DB) ──
        var sw = Stopwatch.StartNew();
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        sc.AddSignalR();
        sc.Configure<AppOptions>(o => { });
        sc.AddDbContextFactory<TambolaDb>(o => o.UseSqlite(cs));
        sc.AddScoped<EmployeeService>();
        sc.AddSingleton<GameService>();
        await using var sp = sc.BuildServiceProvider();
        var emps = sp.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var game = sp.GetRequiredService<GameService>();

        var prizeWinners = 5;
        var patterns = Enum.GetValues<PrizePattern>();
        var roomInfos = new List<RoomInfo>();
        for (var r = 0; r < rooms; r++)
        {
            var hostName = $"host{r}";
            if (await emps.RegisterAsync(@"CORP\" + hostName, hostName, $"H{id}{r}", hostName + "@x.com") is { } e1) throw new Exception(e1);
            var hostId = (await emps.FindByWindowsUserAsync(@"CORP\" + hostName))!.Id;
            await emps.SetHostAsync("load", hostId, true);
            var code = await game.CreateRoomAsync(hostId, new CreateRoomRequest($"Load {r}", GameService.MaxPlayersLimit, 2, CallMode.Manual, 3, CutMode.Auto,
                patterns.Select(p => new PrizeRequest(p, prizeWinners, null)).ToList()));
            var ri = new RoomInfo { Code = code, HostUser = hostName };
            for (var p = 0; p < players; p++)
            {
                var u = $"p{r}x{p}";
                if (await emps.RegisterAsync(@"CORP\" + u, u, $"P{id}{r}-{p}", u + "@x.com") is { } e2) throw new Exception(e2);
                ri.PlayerUsers.Add(u);
            }
            roomInfos.Add(ri);
        }
        res.SeedSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
        Console.WriteLine($"seeded in {res.SeedSeconds}s");

        // ── resource sampler for the server process ──
        double peakCpu = 0, cpuSum = 0; int cpuN = 0; long peakMem = 0;
        using var samplerCts = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            var lastCpu = server.TotalProcessorTime; var lastT = Stopwatch.GetTimestamp();
            while (!samplerCts.IsCancellationRequested)
            {
                try { await Task.Delay(500, samplerCts.Token); } catch { break; }
                try
                {
                    server.Refresh();
                    var cpu = server.TotalProcessorTime; var t = Stopwatch.GetTimestamp();
                    var pct = (cpu - lastCpu).TotalSeconds / Stopwatch.GetElapsedTime(lastT, t).TotalSeconds / Environment.ProcessorCount * 100;
                    lastCpu = cpu; lastT = t;
                    peakCpu = Math.Max(peakCpu, pct); cpuSum += pct; cpuN++;
                    peakMem = Math.Max(peakMem, server.WorkingSet64);
                } catch { }
            }
        });

        // ── connect everyone ──
        HubConnection Make(string user) => new HubConnectionBuilder()
            .WithUrl($"http://127.0.0.1:{port}/hubs/game", o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.Headers["Cookie"] = "dev-user=CORP%5C" + user;
            })
            .WithServerTimeout(TimeSpan.FromSeconds(180)).Build();

        var lat = new Lat();
        long connectFail = 0, opFail = 0;
        var errs = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        void Err(string k, Exception ex) { Interlocked.Increment(ref opFail); errs.AddOrUpdate(k + ": " + ex.Message[..Math.Min(90, ex.Message.Length)], 1, (_, v) => v + 1); }

        async Task<T?> Timed<T>(List<double> bag, string name, Func<Task<T>> f)
        {
            var t0 = Stopwatch.GetTimestamp();
            try { var v = await f(); lock (bag) bag.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds); return v; }
            catch (Exception ex) { Err(name, ex); return default; }
        }
        async Task ForAll<T>(IEnumerable<T> items, int par, Func<T, Task> f)
        {
            using var gate = new SemaphoreSlim(par);
            await Task.WhenAll(items.ToList().Select(async x => { await gate.WaitAsync(); try { await f(x); } finally { gate.Release(); } }));
        }

        var clients = new List<Client>();
        foreach (var ri in roomInfos)
        {
            clients.Add(new Client { Room = ri, User = ri.HostUser, IsHost = true });
            clients.AddRange(ri.PlayerUsers.Select(u => new Client { Room = ri, User = u }));
        }
        res.Clients = clients.Count;
        var total = Stopwatch.StartNew();

        // number-call broadcast latency bookkeeping: sentAt[room][seq]
        foreach (var ri in roomInfos) ri.SentAt = new long[100];
        long delivered = 0;

        await ForAll(clients, 100, async c =>
        {
            c.Hub = Make(c.User);
            if (!c.IsHost)
                c.Hub.On<JsonElement>("NumberCalled", m =>
                {
                    var now = Stopwatch.GetTimestamp();
                    var seq = m.GetProperty("sequence").GetInt32();
                    var sent = c.Room.SentAt![seq];
                    if (sent != 0) lock (lat.Broadcast) lat.Broadcast.Add(Stopwatch.GetElapsedTime(sent, now).TotalMilliseconds);
                    Interlocked.Increment(ref delivered);
                });
            var t0 = Stopwatch.GetTimestamp();
            try { await c.Hub.StartAsync(); lock (lat.Connect) lat.Connect.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds); }
            catch (Exception ex) { Interlocked.Increment(ref connectFail); Err("connect", ex); c.Dead = true; }
        });
        res.ConnectFailures = (int)connectFail;
        Console.WriteLine($"connected {clients.Count(c => !c.Dead)}/{clients.Count} (peak server mem {peakMem >> 20} MB)");
        var live = clients.Where(c => !c.Dead).ToList();

        // ── join + buy 2 tickets ──
        await ForAll(live, 200, async c =>
        {
            await Timed(lat.Join, "join", () => c.Hub!.InvokeAsync<RoomStateDto>("JoinRoom", c.Room.Code));
        });
        Console.WriteLine("joined");
        await ForAll(live.Where(c => !c.IsHost), 200, async c =>
        {
            for (var k = 0; k < 2; k++)
            {
                var t = await Timed(lat.Buy, "buy", () => c.Hub!.InvokeAsync<TicketDto>("BuyTicket", c.Room.Code));
                if (t != null && k == 0) c.TicketId = t.id;
            }
        });
        Console.WriteLine("tickets bought");

        // ── start + call all 90 numbers in every room in parallel ──
        await Task.WhenAll(live.Where(c => c.IsHost).Select(h => Timed(lat.Start, "start", () => h.Hub!.InvokeAsync<bool>("Start", h.Room.Code))));
        var playersLive = live.Count(c => !c.IsHost);
        await Task.WhenAll(live.Where(c => c.IsHost).Select(async h =>
        {
            for (var n = 1; n <= 90; n++)
            {
                h.Room.SentAt![n] = Stopwatch.GetTimestamp();
                await Timed(lat.Call, "call", () => h.Hub!.InvokeAsync<bool>("CallNext", h.Room.Code));
                await Task.Delay(callGapMs);
            }
        }));
        await Task.Delay(Math.Max(3000, playersLive / 2));   // let the last broadcasts drain
        var expected = (long)playersLive * 90;
        res.BroadcastDelivered = delivered; res.BroadcastExpected = expected;
        Console.WriteLine($"numbers delivered {delivered}/{expected}");

        // ── claim storms: every player claims every pattern at once, one pattern at a time ──
        foreach (var pat in patterns)
        {
            var statuses = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
            var bag = new List<double>();
            await ForAll(live.Where(c => !c.IsHost && c.TicketId != 0), 300, async c =>
            {
                var o = await Timed(bag, "claim", () => c.Hub!.InvokeAsync<ClaimOutcome>("Claim", c.Room.Code, c.TicketId, pat.ToString()));
                if (o != null) statuses.AddOrUpdate(o.status, 1, (_, v) => v + 1);
            });
            lat.Claim.AddRange(bag);
            var won = statuses.GetValueOrDefault("won");
            var expectedWins = Math.Min(prizeWinners, players) * rooms;
            res.ClaimCases.Add(new ClaimCase
            {
                Pattern = pat.ToString(), Won = won, ExpectedWon = expectedWins, Late = statuses.GetValueOrDefault("late"),
                Other = statuses.Where(k => k.Key is not ("won" or "late")).Sum(k => k.Value),
                P50 = Pct(bag, 50), P95 = Pct(bag, 95), Max = Math.Round(bag.Count == 0 ? 0 : bag.Max(), 0),
            });
            Console.WriteLine($"claim {pat}: won={won} (expected {expectedWins}) late={statuses.GetValueOrDefault("late")} p95={Pct(bag, 95)}ms");
        }

        samplerCts.Cancel(); await sampler;
        res.TotalSeconds = Math.Round(total.Elapsed.TotalSeconds, 1);
        res.Connect = Summ(lat.Connect); res.Join = Summ(lat.Join); res.Buy = Summ(lat.Buy);
        res.Call = Summ(lat.Call); res.Broadcast = Summ(lat.Broadcast); res.Claim = Summ(lat.Claim);
        res.OpFailures = (int)opFail; res.Errors = errs.OrderByDescending(k => k.Value).Take(6).ToDictionary(k => k.Key, k => k.Value);
        res.ServerPeakCpuPct = Math.Round(peakCpu, 0); res.ServerAvgCpuPct = Math.Round(cpuN == 0 ? 0 : cpuSum / cpuN, 0);
        res.ServerPeakMemMb = peakMem >> 20;
        res.ServerAlive = !server.HasExited;
        res.ClaimsCorrect = res.ClaimCases.All(c => c.Won == c.ExpectedWon);
        res.DeliveryPct = expected == 0 ? 0 : Math.Round(100.0 * delivered / expected, 2);
        res.P95BroadcastOk = res.Broadcast.P95 < 1000;   // NFR-1: < 1 s end-to-end
        res.Pass = res.ConnectFailures == 0 && res.OpFailures == 0 && res.ClaimsCorrect && res.DeliveryPct >= 99.99 && res.P95BroadcastOk && res.ServerAlive;
        lock (serverErr) if (serverErr.Length > 0) res.Notes = serverErr.ToString()[..Math.Min(400, serverErr.Length)];

        await Task.WhenAll(clients.Where(c => c.Hub != null).Select(async c => { try { await c.Hub!.StopAsync(); } catch { } }));
    }
    finally
    {
        try { server.Kill(true); server.WaitForExit(5000); } catch { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(db) + "*")) try { File.Delete(f); } catch { }
    }
    return res;
}

static double Pct(List<double> v, double p)
{
    if (v.Count == 0) return 0;
    double[] a; lock (v) a = v.OrderBy(x => x).ToArray();
    return Math.Round(a[Math.Min(a.Length - 1, (int)Math.Ceiling(p / 100 * a.Length) - 1)], 0);
}
static Summary Summ(List<double> v) => new() { N = v.Count, P50 = Pct(v, 50), P95 = Pct(v, 95), P99 = Pct(v, 99), Max = v.Count == 0 ? 0 : Math.Round(v.Max(), 0) };

class Lat { public List<double> Connect = new(), Join = new(), Buy = new(), Start = new(), Call = new(), Broadcast = new(), Claim = new(); }
class RoomInfo { public string Code = ""; public string HostUser = ""; public List<string> PlayerUsers = new(); public long[]? SentAt; }
class Client { public RoomInfo Room = null!; public string User = ""; public bool IsHost, Dead; public HubConnection? Hub; public int TicketId; }
class Summary { public int N { get; set; } public double P50 { get; set; } public double P95 { get; set; } public double P99 { get; set; } public double Max { get; set; } }
class ClaimCase { public string Pattern { get; set; } = ""; public int Won { get; set; } public int ExpectedWon { get; set; } public int Late { get; set; } public int Other { get; set; } public double P50 { get; set; } public double P95 { get; set; } public double Max { get; set; } }
class Result
{
    public string Id { get; set; } = ""; public int Rooms { get; set; } public int PlayersPerRoom { get; set; } public int Clients { get; set; }
    public double SeedSeconds { get; set; } public double TotalSeconds { get; set; }
    public int ConnectFailures { get; set; } public int OpFailures { get; set; }
    public long BroadcastDelivered { get; set; } public long BroadcastExpected { get; set; } public double DeliveryPct { get; set; }
    public Summary Connect { get; set; } = new();
    public Summary Join { get; set; } = new();
    public Summary Buy { get; set; } = new();
    public Summary Call { get; set; } = new();
    public Summary Broadcast { get; set; } = new();
    public Summary Claim { get; set; } = new();
    public List<ClaimCase> ClaimCases { get; set; } = new();
    public bool ClaimsCorrect { get; set; } public bool P95BroadcastOk { get; set; } public bool ServerAlive { get; set; } public bool Pass { get; set; }
    public double ServerPeakCpuPct { get; set; } public double ServerAvgCpuPct { get; set; } public long ServerPeakMemMb { get; set; }
    public Dictionary<string, int> Errors { get; set; } = new(); public string Notes { get; set; } = "";
}
