using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Tambola.Domain.Entities;
using Tambola.Web.Auth;
using Tambola.Web.Data;
using Tambola.Web.Services;

namespace Tambola.Tests;

/// <summary>End-to-end game flow through the real SignalR hub, auth and SQLite.</summary>
public class GameFlowTests : IAsyncLifetime
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"tambola-test-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Tambola"] = $"Data Source={_db};Default Timeout=30",
                ["Tambola:AuthMode"] = "Dev",
                ["Tambola:AdminUsers:0"] = @"CORP\admin",
            }));
        });
        _ = _factory.Server; // start
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_db) + "*")) try { File.Delete(f); } catch { }
    }

    private async Task<int> Register(string user, string code)
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        Assert.Null(await emps.RegisterAsync(@"CORP\" + user, user, code, user + "@company.com"));
        return (await emps.FindByWindowsUserAsync(@"CORP\" + user))!.Id;
    }

    private async Task<HubConnection> Connect(string user)
    {
        var hub = new HubConnectionBuilder().WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/game"), o =>
        {
            o.Transports = HttpTransportType.LongPolling;
            o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            o.Headers["Cookie"] = "dev-user=CORP%5C" + user;
        }).Build();
        await hub.StartAsync();
        return hub;
    }

    [Fact]
    public async Task Duplicate_code_or_email_is_rejected()
    {
        await Register("alice", "EMP001");
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        Assert.Contains("Employee Code", (await emps.RegisterAsync(@"CORP\bob", "Bob", "emp001", "bob@company.com"))!);
        Assert.Contains("email", (await emps.RegisterAsync(@"CORP\bob", "Bob", "EMP002", "alice@company.com"))!);
        Assert.Contains("Windows account", (await emps.RegisterAsync(@"CORP\alice", "Alice", "EMP003", "other@company.com"))!);
    }

    [Fact]
    public async Task Full_game_claims_quota_locking_and_authorization()
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("host1", "EMPH1");
        await Register("p1", "EMPP1"); await Register("p2", "EMPP2"); await Register("p3", "EMPP3");
        await emps.SetHostAsync("test", hostId, true);

        var game = _factory.Services.GetRequiredService<GameService>();
        var code = await game.CreateRoomAsync(hostId, new CreateRoomRequest("Test room", 50, 2, CallMode.Manual, 6, CutMode.Auto, new()
        {
            new(PrizePattern.FullHouse, 1, "Hamper"), new(PrizePattern.FirstRow, 5, null)
        }, MaxWrongClaims: 1));   // one strike: the original behaviour

        await using var host = await Connect("host1");
        await using var p1 = await Connect("p1"); await using var p2 = await Connect("p2"); await using var p3 = await Connect("p3");

        var hs = await host.InvokeAsync<RoomStateDto>("JoinRoom", code);
        Assert.True(hs.isHost);
        var s1 = await p1.InvokeAsync<RoomStateDto>("JoinRoom", code);
        await p2.InvokeAsync<RoomStateDto>("JoinRoom", code);
        await p3.InvokeAsync<RoomStateDto>("JoinRoom", code);
        Assert.False(s1.isHost);

        // players cannot use host methods
        await Assert.ThrowsAsync<HubException>(() => p1.InvokeAsync<bool>("Start", code));
        // cannot start without tickets
        await Assert.ThrowsAsync<HubException>(() => host.InvokeAsync<bool>("Start", code));

        var t1 = await p1.InvokeAsync<TicketDto>("BuyTicket", code);
        var t2 = await p2.InvokeAsync<TicketDto>("BuyTicket", code);
        var t3 = await p3.InvokeAsync<TicketDto>("BuyTicket", code);
        await p3.InvokeAsync<TicketDto>("BuyTicket", code);
        await Assert.ThrowsAsync<HubException>(() => p3.InvokeAsync<TicketDto>("BuyTicket", code)); // limit 2

        await host.InvokeAsync<bool>("Start", code);

        // early claim = bogey, then the prize is locked for that player
        var bogey = await p2.InvokeAsync<ClaimOutcome>("Claim", code, t2.id, "FullHouse");
        Assert.Equal("invalid", bogey.status);
        var again = await p2.InvokeAsync<ClaimOutcome>("Claim", code, t2.id, "FullHouse");
        Assert.Equal("rejected", again.status);

        // cannot claim with someone else's ticket
        var stolen = await p3.InvokeAsync<ClaimOutcome>("Claim", code, t1.id, "FirstRow");
        Assert.Equal("rejected", stolen.status);

        // snapshot never exposes uncalled numbers
        var calledEvents = new List<int>();
        p1.On<object>("NumberCalled", _ => { });
        for (var i = 1; i <= 90; i++) await host.InvokeAsync<bool>("CallNext", code);
        await Assert.ThrowsAsync<HubException>(() => host.InvokeAsync<bool>("CallNext", code)); // 91st
        var snap = await p1.InvokeAsync<RoomStateDto>("GetState", code);
        Assert.Equal(90, snap.called.Length);
        Assert.Equal(Enumerable.Range(1, 90), snap.called.OrderBy(x => x));

        // winner quota: Full House has 1 slot
        var won = await p1.InvokeAsync<ClaimOutcome>("Claim", code, t1.id, "FullHouse");
        Assert.Equal("won", won.status); Assert.Equal(1, won.slot); Assert.Equal("Hamper", won.reward);
        var late = await p3.InvokeAsync<ClaimOutcome>("Claim", code, t3.id, "FullHouse");
        Assert.Equal("late", late.status);

        // already won -> rejected, and the lock survives a fresh snapshot
        Assert.Equal("rejected", (await p1.InvokeAsync<ClaimOutcome>("Claim", code, t1.id, "FullHouse")).status);
        var after = await p1.InvokeAsync<RoomStateDto>("GetState", code);
        Assert.Equal("won", after.mine["FullHouse"]);
        Assert.Equal("barred", (await p2.InvokeAsync<RoomStateDto>("GetState", code)).mine["FullHouse"]);

        // First Row has 5 slots: p1, p3 win; p2 can still claim (not barred for this prize)
        Assert.Equal("won", (await p1.InvokeAsync<ClaimOutcome>("Claim", code, t1.id, "FirstRow")).status);
        Assert.Equal("won", (await p2.InvokeAsync<ClaimOutcome>("Claim", code, t2.id, "FirstRow")).slot == 2 ? "won" : "x");
        var fin = await host.InvokeAsync<RoomStateDto>("GetState", code);
        Assert.Equal("Running", fin.status);                 // First Row not full yet
        Assert.Single(fin.prizes.First(p => p.pattern == "FullHouse").winners);

        await host.InvokeAsync<bool>("End", code, false);
        Assert.Equal("Completed", (await host.InvokeAsync<RoomStateDto>("GetState", code)).status);
    }

    // ───────────── v1.1: wrong-claim limit, 1500 cap, delete, domain validator, passwords ─────────────

    private async Task<(string code, HubConnection host, HubConnection player, TicketDto ticket)> StartedRoom(string tag, int maxWrong, int calls = 3)
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("h" + tag, "EH" + tag); await Register("p" + tag, "EP" + tag);
        await emps.SetHostAsync("t", hostId, true);
        var game = _factory.Services.GetRequiredService<GameService>();
        var code = await game.CreateRoomAsync(hostId, new CreateRoomRequest("Room " + tag, 10, 1, CallMode.Manual, 6, CutMode.Auto,
            new() { new(PrizePattern.FullHouse, 1, null), new(PrizePattern.FirstRow, 1, null) }, maxWrong));
        var host = await Connect("h" + tag); var player = await Connect("p" + tag);
        await host.InvokeAsync<RoomStateDto>("JoinRoom", code); await player.InvokeAsync<RoomStateDto>("JoinRoom", code);
        var t = await player.InvokeAsync<TicketDto>("BuyTicket", code);
        await host.InvokeAsync<bool>("Start", code);
        for (var i = 0; i < calls; i++) await host.InvokeAsync<bool>("CallNext", code);
        return (code, host, player, t);
    }

    [Fact]
    public async Task Wrong_claims_lock_the_prize_only_after_the_room_limit()
    {
        var (code, host, p, t) = await StartedRoom("w3", 3);
        await using var _h = host; await using var _p = p;

        var c1 = await p.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FullHouse");
        Assert.Equal("invalid", c1.status); Assert.False(c1.locked); Assert.Equal(2, c1.left);
        // a non-locked player can still try again and the state does not show the prize as barred
        Assert.False((await p.InvokeAsync<RoomStateDto>("GetState", code)).mine.ContainsKey("FullHouse"));
        var c2 = await p.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FullHouse");
        Assert.Equal("invalid", c2.status); Assert.False(c2.locked); Assert.Equal(1, c2.left);
        var c3 = await p.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FullHouse");
        Assert.Equal("invalid", c3.status); Assert.True(c3.locked); Assert.Equal(0, c3.left);
        var c4 = await p.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FullHouse");
        Assert.Equal("rejected", c4.status); Assert.True(c4.locked);
        var st = await p.InvokeAsync<RoomStateDto>("GetState", code);
        Assert.Equal("barred", st.mine["FullHouse"]); Assert.Equal(3, st.maxWrongClaims);
        // strikes are per prize: First Row is still open for this player
        Assert.Equal("invalid", (await p.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FirstRow")).status);
        Assert.False((await p.InvokeAsync<RoomStateDto>("GetState", code)).mine.ContainsKey("FirstRow"));
    }

    [Fact]
    public async Task Unlimited_wrong_claims_never_lock()
    {
        var (code, host, p, t) = await StartedRoom("w0", 0);
        await using var _h = host; await using var _p = p;
        for (var i = 0; i < 12; i++)
        {
            var c = await p.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FullHouse");
            Assert.Equal("invalid", c.status); Assert.False(c.locked); Assert.Equal(-1, c.left);
        }
        Assert.False((await p.InvokeAsync<RoomStateDto>("GetState", code)).mine.ContainsKey("FullHouse"));
    }

    [Fact]
    public async Task Room_size_is_capped_at_1500_players()
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("hcap", "EHCAP"); await emps.SetHostAsync("t", hostId, true);
        var game = _factory.Services.GetRequiredService<GameService>();
        var prizes = new List<PrizeRequest> { new(PrizePattern.FullHouse, 1, null) };
        var a = await game.CreateRoomAsync(hostId, new CreateRoomRequest("big", 1500, 1, CallMode.Manual, 6, CutMode.Auto, prizes));
        var b = await game.CreateRoomAsync(hostId, new CreateRoomRequest("too big", 5000, 1, CallMode.Manual, 6, CutMode.Auto, prizes));
        Assert.Equal(1500, (await game.FindRoomAsync(a))!.MaxPlayers);
        Assert.Equal(1500, (await game.FindRoomAsync(b))!.MaxPlayers);   // clamped, not rejected
        Assert.Equal(3, (await game.FindRoomAsync(a))!.MaxWrongClaims);  // default
    }

    [Fact]
    public async Task Deleting_a_finished_game_hides_it_from_users_but_an_admin_can_restore_it()
    {
        var (code, host, p, t) = await StartedRoom("del", 3);
        await using var _h = host; await using var _p = p;
        var game = _factory.Services.GetRequiredService<GameService>();
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = (await emps.FindByWindowsUserAsync(@"CORP\hdel"))!.Id;
        var playerId = (await emps.FindByWindowsUserAsync(@"CORP\pdel"))!.Id;

        // not while the game is live; not by a player
        await Assert.ThrowsAsync<GameException>(() => game.DeleteRoomAsync(code, hostId, @"CORP\hdel", false));
        await host.InvokeAsync<bool>("End", code, false);
        await Assert.ThrowsAsync<GameException>(() => game.DeleteRoomAsync(code, playerId, @"CORP\pdel", false));

        await game.DeleteRoomAsync(code, hostId, @"CORP\hdel", false);
        Assert.Null(await game.FindRoomAsync(code));                                           // hidden for pages
        await Assert.ThrowsAsync<HubException>(() => p.InvokeAsync<RoomStateDto>("GetState", code));   // and for the hub
        await using (var db = await _factory.Services.GetRequiredService<IDbContextFactory<TambolaDb>>().CreateDbContextAsync())
        {
            Assert.False(await db.Rooms.AnyAsync(r => r.Code == code));                           // default queries never see it
            Assert.True(await db.Rooms.IgnoreQueryFilters().AnyAsync(r => r.Code == code && r.IsDeleted && r.DeletedBy == @"CORP\hdel"));   // Admin reports do
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "DeleteRoom"));
        }

        await game.RestoreRoomAsync(code, @"CORP\admin");
        Assert.NotNull(await game.FindRoomAsync(code));
    }

    [Fact]
    public void Domain_validator_allows_only_listed_domains()
    {
        var o = new AppOptions { AllowedDomains = new[] { "CORP", "Sub" } };
        Assert.True(o.IsDomainAllowed(@"corp\alice")); Assert.True(o.IsDomainAllowed(@"SUB\bob"));
        Assert.False(o.IsDomainAllowed(@"OTHER\eve")); Assert.False(o.IsDomainAllowed("alice")); Assert.False(o.IsDomainAllowed(@"\x"));
        Assert.True(new AppOptions().IsDomainAllowed(@"ANY\one"));   // no restriction configured
    }

    [Fact]
    public async Task Registration_from_a_blocked_domain_is_refused_and_manual_accounts_validate()
    {
        var opt = new AppOptions { AllowedDomains = new[] { "CORP" } };
        var emps = new EmployeeService(_factory.Services.GetRequiredService<IDbContextFactory<TambolaDb>>(), Microsoft.Extensions.Options.Options.Create(opt));
        Assert.Contains("domain", (await emps.RegisterAsync(@"EVIL\x", "Evil", "EVIL1", "e@x.com"))!);
        Assert.Contains("valid domain", emps.BuildAccount("EVIL", "x", out _)!);
        Assert.Contains("User ID", emps.BuildAccount("CORP", "a b", out _)!);
        Assert.Null(emps.BuildAccount("corp", " Sumit.H ", out var acct)); Assert.Equal(@"CORP\sumit.h", acct);   // domain case from config, userID lowercased

        Assert.Contains("at least", (await emps.RegisterAsync(@"CORP\pw", "Pw", "PW1", "pw@x.com", "short"))!);
        Assert.Null(await emps.RegisterAsync(@"CORP\pw", "Pw", "PW1", "pw@x.com", "correct horse"));
        Assert.NotNull(await emps.VerifyLoginAsync(@"CORP\pw", "correct horse"));
        Assert.Null(await emps.VerifyLoginAsync(@"CORP\pw", "wrong password"));
        Assert.Null(await emps.VerifyLoginAsync(@"CORP\nobody", "correct horse"));

        // admin reset: temporary password works once and forces a change
        var id = (await emps.FindByWindowsUserAsync(@"CORP\pw"))!.Id;
        var temp = await emps.ResetPasswordAsync("admin", id);
        Assert.True((await emps.VerifyLoginAsync(@"CORP\pw", temp))!.MustChangePassword);
        Assert.Null(await emps.VerifyLoginAsync(@"CORP\pw", "correct horse"));
        Assert.Null(await emps.ChangePasswordAsync(@"CORP\pw", id, "a brand new one", false));
        Assert.False((await emps.VerifyLoginAsync(@"CORP\pw", "a brand new one"))!.MustChangePassword);
    }

    [Fact]
    public void Password_hash_roundtrip_and_login_throttle()
    {
        var h = PasswordHasher.Hash("s3cret-pass", 1000);
        Assert.True(PasswordHasher.Verify("s3cret-pass", h)); Assert.False(PasswordHasher.Verify("S3cret-pass", h));
        Assert.NotEqual(h, PasswordHasher.Hash("s3cret-pass", 1000));            // random salt
        Assert.False(PasswordHasher.Verify("x", null)); Assert.False(PasswordHasher.Verify("x", "garbage"));

        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var th = new LoginThrottle(() => now);
        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++) { th.RecordFailure(@"CORP\a", "1.1.1.1"); Assert.False(th.IsLocked(@"CORP\a", "1.1.1.1", out _)); }
        th.RecordFailure(@"CORP\a", "1.1.1.1");
        Assert.True(th.IsLocked(@"CORP\a", "1.1.1.1", out var wait)); Assert.Equal(LoginThrottle.LockFor, wait);
        Assert.False(th.IsLocked(@"CORP\a", "2.2.2.2", out _));                   // other IP unaffected
        Assert.False(th.IsLocked(@"CORP\b", "1.1.1.1", out _));                   // other account unaffected
        now = now.Add(LoginThrottle.LockFor).AddSeconds(1);
        Assert.False(th.IsLocked(@"CORP\a", "1.1.1.1", out _));                   // lockout expires
        th.RecordFailure(@"CORP\a", "1.1.1.1"); Assert.False(th.IsLocked(@"CORP\a", "1.1.1.1", out _));   // and the count restarts
    }

    [Fact]
    public async Task Existing_database_is_upgraded_in_place_and_keeps_its_data()
    {
        // an "old" database: created by the previous version (no new columns), with one room in it
        var path = Path.Combine(Path.GetTempPath(), $"tambola-old-{Guid.NewGuid():N}.db");
        try
        {
            await using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await c.OpenAsync();
                var sql = new[]
                {
                    "CREATE TABLE Employees (Id INTEGER PRIMARY KEY AUTOINCREMENT, WindowsUser TEXT NOT NULL, EmployeeCode TEXT NOT NULL, Email TEXT NOT NULL, DisplayName TEXT NOT NULL, IsActive INTEGER NOT NULL, IsHost INTEGER NOT NULL, RegisteredAt TEXT NOT NULL)",
                    "CREATE TABLE Rooms (Id INTEGER PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL, Name TEXT NOT NULL, HostId INTEGER NOT NULL, Status INTEGER NOT NULL, CallMode INTEGER NOT NULL, CutMode INTEGER NOT NULL, CallIntervalSeconds INTEGER NOT NULL, MaxPlayers INTEGER NOT NULL, MaxTicketsPerPlayer INTEGER NOT NULL, IsLocked INTEGER NOT NULL, DrawOrder TEXT NOT NULL, NextCallAt TEXT NULL, CreatedAt TEXT NOT NULL, StartedAt TEXT NULL, EndedAt TEXT NULL)",
                    "CREATE TABLE Tickets (Id INTEGER PRIMARY KEY AUTOINCREMENT, RoomId INTEGER NOT NULL, EmployeeId INTEGER NOT NULL, Serial INTEGER NOT NULL, Grid TEXT NOT NULL, Marks TEXT NOT NULL)",
                    "INSERT INTO Employees VALUES (1,'CORP\\old','OLD1','old@x.com','Old',1,1,'2026-01-01')",
                    "INSERT INTO Rooms VALUES (1,'OLDRM1','Old room',1,3,1,1,6,100,3,0,'1,2,3',NULL,'2026-01-01',NULL,NULL)",
                };
                foreach (var s in sql) { await using var cmd = c.CreateCommand(); cmd.CommandText = s; await cmd.ExecuteNonQueryAsync(); }
            }
            var options = new DbContextOptionsBuilder<TambolaDb>().UseSqlite($"Data Source={path}").Options;
            await using (var db = new TambolaDb(options))
            {
                await SchemaUpgrader.UpgradeAsync(db);
                await SchemaUpgrader.UpgradeAsync(db);   // idempotent
                var room = await db.Rooms.SingleAsync(r => r.Code == "OLDRM1");
                Assert.Equal(1, room.MaxWrongClaims);    // old rooms keep the one-strike rule
                Assert.False(room.IsDeleted);
                Assert.Equal("Old room", room.Name);
                Assert.Null((await db.Employees.SingleAsync()).PasswordHash);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
    }

    /// <summary>Starts the app in Manual (Linux) auth mode. AuthMode is read while Program.cs starts, so it must come from environment variables.</summary>
    private WebApplicationFactory<Program> ManualFactory(string dbSuffix, params string[] domains) => AuthFactory("Manual", dbSuffix, domains);

    private WebApplicationFactory<Program> AuthFactory(string mode, string dbSuffix, params string[] domains)
    {
        var vars = new Dictionary<string, string?> { ["Tambola__AuthMode"] = mode, ["ConnectionStrings__Tambola"] = $"Data Source={_db}-{dbSuffix};Default Timeout=30" };
        for (var i = 0; i < domains.Length; i++) vars[$"Tambola__AllowedDomains__{i}"] = domains[i];
        foreach (var kv in vars) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        try
        {
            var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development"));
            _ = f.Server;   // start now, while the variables are set
            return f;
        }
        finally { foreach (var kv in vars) Environment.SetEnvironmentVariable(kv.Key, null); }
    }

    [Fact]
    public async Task Users_from_a_domain_that_is_not_allowed_are_sent_to_the_blocked_page()
    {
        // same code path as Windows auth: the claims transformation sees DOMAIN\user from whatever scheme authenticated them
        await using var f = AuthFactory("Dev", "blocked", "CORP");
        var http = f.CreateClient(new() { AllowAutoRedirect = false });
        HttpRequestMessage As(string user, string url) { var r = new HttpRequestMessage(HttpMethod.Get, url); r.Headers.Add("Cookie", "dev-user=" + Uri.EscapeDataString(user)); return r; }

        var blocked = await http.SendAsync(As(@"EVIL\eve", "/"));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Equal("/Blocked", blocked.Headers.Location!.OriginalString);
        var page = await http.SendAsync(As(@"EVIL\eve", "/Blocked"));
        Assert.Equal(System.Net.HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("EVIL", await page.Content.ReadAsStringAsync());
        // an allowed domain is not blocked (an unregistered one is sent to registration instead)
        var allowed = await http.SendAsync(As(@"CORP\bob", "/"));
        Assert.Equal("/Register", allowed.Headers.Location!.OriginalString);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var x in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_db) + "-blocked*")) try { File.Delete(x); } catch { }
    }

    [Fact]
    public async Task Manual_mode_redirects_visitors_to_the_login_page()
    {
        await using var f = ManualFactory("manual", "CORP", "SUB");
        var http = f.CreateClient(new() { AllowAutoRedirect = false });
        var home = await http.GetAsync("/");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, home.StatusCode);
        Assert.StartsWith("/Account/Login", home.Headers.Location!.OriginalString.Replace("http://localhost", ""));
        var login = await http.GetAsync("/Account/Login");
        Assert.Equal(System.Net.HttpStatusCode.OK, login.StatusCode);
        var html = await login.Content.ReadAsStringAsync();
        Assert.Contains("CORP", html); Assert.Contains("SUB", html);       // domain dropdown
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("/Account/Register")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await http.PostAsync("/hubs/game/negotiate?negotiateVersion=1", null)).StatusCode);   // hub: 401, not a redirect
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var x in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_db) + "-manual*")) try { File.Delete(x); } catch { }
    }

    [Fact]
    public async Task Manual_mode_register_login_and_logout_flow()
    {
        await using var f = ManualFactory("flow", "CORP");
        var http = f.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

        async Task<HttpResponseMessage> Post(string url, Dictionary<string, string> form)
        {
            var page = await (await http.GetAsync(url)).Content.ReadAsStringAsync();
            var m = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(m.Success, "antiforgery token on " + url);
            form["__RequestVerificationToken"] = m.Groups[1].Value;
            return await http.PostAsync(url, new FormUrlEncodedContent(form));
        }

        // wrong confirm / weak password are refused on the page
        var bad = await Post("/Account/Register", new() { ["Domain"] = "CORP", ["UserId"] = "sam", ["Password"] = "longenough1", ["ConfirmPassword"] = "different", ["DisplayName"] = "Sam", ["EmployeeCode"] = "S100", ["Email"] = "sam@other.org" });
        Assert.Contains("do not match", await bad.Content.ReadAsStringAsync());

        var ok = await Post("/Account/Register", new() { ["Domain"] = "CORP", ["UserId"] = "Sam", ["Password"] = "longenough1", ["ConfirmPassword"] = "longenough1", ["DisplayName"] = "Sam", ["EmployeeCode"] = "S100", ["Email"] = "sam@other.org" });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, ok.StatusCode);                    // registered + signed in
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("/")).StatusCode);   // home is reachable

        // the Sign out button in the layout must carry an anti-forgery token (a plain action= form does not, and the POST is then rejected)
        var homeHtml = await (await http.GetAsync("/")).Content.ReadAsStringAsync();
        var logoutForm = System.Text.RegularExpressions.Regex.Match(homeHtml, "<form[^>]*Logout[^>]*>.*?</form>", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(logoutForm.Success, "Sign out form is on the page");
        Assert.Contains("__RequestVerificationToken", logoutForm.Value);
        var layoutToken = System.Text.RegularExpressions.Regex.Match(logoutForm.Value, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var viaLayout = await http.PostAsync("/Account/ChangePassword?handler=Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = layoutToken }));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, viaLayout.StatusCode);     // signed out using the layout's own form
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await http.GetAsync("/")).StatusCode);
        var relog = await Post("/Account/Login", new() { ["Domain"] = "CORP", ["UserId"] = "sam", ["Password"] = "longenough1" });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, relog.StatusCode);

        // sign out, then a wrong password is refused and the right one works
        var logout = await Post("/Account/ChangePassword?handler=Logout", new());
        Assert.Equal(System.Net.HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, (await http.GetAsync("/")).StatusCode);
        var wrong = await Post("/Account/Login", new() { ["Domain"] = "CORP", ["UserId"] = "sam", ["Password"] = "nope-nope-nope" });
        Assert.Contains("Invalid domain, user ID or password", await wrong.Content.ReadAsStringAsync());
        var good = await Post("/Account/Login", new() { ["Domain"] = "CORP", ["UserId"] = "sam", ["Password"] = "longenough1" });
        Assert.Equal(System.Net.HttpStatusCode.Redirect, good.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync("/")).StatusCode);

        // a domain that is not on the list cannot register at all (sign out first: signed-in users are redirected away from the form)
        await Post("/Account/ChangePassword?handler=Logout", new());
        var evil = await Post("/Account/Register", new() { ["Domain"] = "EVIL", ["UserId"] = "eve", ["Password"] = "longenough1", ["ConfirmPassword"] = "longenough1", ["DisplayName"] = "Eve", ["EmployeeCode"] = "E100", ["Email"] = "eve@x.org" });
        Assert.Contains("valid domain", await evil.Content.ReadAsStringAsync());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var x in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_db) + "-flow*")) try { File.Delete(x); } catch { }
    }

    [Fact]
    public async Task Manual_cut_validates_called_numbers_and_auto_completes_when_all_prizes_full()
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("host2", "EMPH2"); await Register("q1", "EMPQ1");
        await emps.SetHostAsync("t", hostId, true);
        var game = _factory.Services.GetRequiredService<GameService>();
        var code = await game.CreateRoomAsync(hostId, new CreateRoomRequest("Manual room", 10, 1, CallMode.Manual, 6, CutMode.Manual,
            new() { new(PrizePattern.FullHouse, 1, null) }));

        await using var host = await Connect("host2"); await using var q1 = await Connect("q1");
        await host.InvokeAsync<RoomStateDto>("JoinRoom", code); await q1.InvokeAsync<RoomStateDto>("JoinRoom", code);
        var t = await q1.InvokeAsync<TicketDto>("BuyTicket", code);
        await host.InvokeAsync<bool>("Start", code);

        var first = t.grid.First(v => v != 0);
        var ex = await Assert.ThrowsAsync<HubException>(() => q1.InvokeAsync<int[]>("Mark", code, t.id, first));
        Assert.Contains("not been called", ex.Message);

        for (var i = 0; i < 90; i++) await host.InvokeAsync<bool>("CallNext", code);
        var marks = await q1.InvokeAsync<int[]>("Mark", code, t.id, first);
        Assert.Contains(first, marks);
        Assert.Empty(await q1.InvokeAsync<int[]>("Mark", code, t.id, first));            // toggles off

        Assert.Equal("won", (await q1.InvokeAsync<ClaimOutcome>("Claim", code, t.id, "FullHouse")).status);
        Assert.Equal("Completed", (await host.InvokeAsync<RoomStateDto>("GetState", code)).status); // only prize is full
    }

    [Fact]
    public async Task Auto_caller_calls_numbers_and_unregistered_users_are_blocked()
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("host3", "EMPH3"); await Register("r1", "EMPR1");
        await emps.SetHostAsync("t", hostId, true);
        var game = _factory.Services.GetRequiredService<GameService>();
        var code = await game.CreateRoomAsync(hostId, new CreateRoomRequest("Auto room", 10, 1, CallMode.Auto, 3, CutMode.Auto, new() { new(PrizePattern.FullHouse, 1, null) }));

        await using var host = await Connect("host3"); await using var r1 = await Connect("r1");
        await host.InvokeAsync<RoomStateDto>("JoinRoom", code); await r1.InvokeAsync<RoomStateDto>("JoinRoom", code);
        await r1.InvokeAsync<TicketDto>("BuyTicket", code);
        await host.InvokeAsync<bool>("Start", code);
        await Assert.ThrowsAsync<HubException>(() => host.InvokeAsync<bool>("CallNext", code)); // manual call refused in auto mode

        await Task.Delay(TimeSpan.FromSeconds(7));   // first call after ~2s, then every 3s
        var n = (await r1.InvokeAsync<RoomStateDto>("GetState", code)).called.Length;
        Assert.InRange(n, 2, 4);

        // a Windows user who never registered cannot use the hub
        await Assert.ThrowsAnyAsync<Exception>(async () => { await using var ghost = await Connect("ghost"); await ghost.InvokeAsync<RoomStateDto>("JoinRoom", code); });
    }

    // ───────────── v1.2: a prize can be won once per ticket ─────────────

    [Fact]
    public async Task A_prize_can_be_won_once_per_ticket_by_the_same_player()
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("hmt", "EHmt"); await Register("pmt", "EPmt"); await Register("qmt", "EQmt");
        await emps.SetHostAsync("t", hostId, true);
        var game = _factory.Services.GetRequiredService<GameService>();
        var code = await game.CreateRoomAsync(hostId, new CreateRoomRequest("Multi", 10, 3, CallMode.Manual, 6, CutMode.Auto,
            new() { new(PrizePattern.FullHouse, 4, null) }, 3));
        await using var host = await Connect("hmt"); await using var p = await Connect("pmt"); await using var q = await Connect("qmt");
        foreach (var c in new[] { host, p, q }) await c.InvokeAsync<RoomStateDto>("JoinRoom", code);
        var pt = new List<TicketDto>(); for (var i = 0; i < 3; i++) pt.Add(await p.InvokeAsync<TicketDto>("BuyTicket", code));
        var qt = await q.InvokeAsync<TicketDto>("BuyTicket", code);
        await host.InvokeAsync<bool>("Start", code);
        for (var i = 0; i < 90; i++) await host.InvokeAsync<bool>("CallNext", code);

        var w1 = await p.InvokeAsync<ClaimOutcome>("Claim", code, pt[0].id, "FullHouse");
        Assert.Equal("won", w1.status); Assert.Equal(1, w1.slot);
        // the same ticket cannot win the same prize twice
        Assert.Equal("rejected", (await p.InvokeAsync<ClaimOutcome>("Claim", code, pt[0].id, "FullHouse")).status);
        // but the same player can win it again with another ticket
        var w2 = await p.InvokeAsync<ClaimOutcome>("Claim", code, pt[1].id, "FullHouse");
        Assert.Equal("won", w2.status); Assert.Equal(2, w2.slot);
        var mid = await p.InvokeAsync<RoomStateDto>("GetState", code);
        Assert.False(mid.mine.ContainsKey("FullHouse"));                       // one ticket has not won yet -> still claimable
        Assert.Equal(new[] { pt[0].id, pt[1].id }, mid.wonTickets!["FullHouse"].OrderBy(x => x));
        var w3 = await p.InvokeAsync<ClaimOutcome>("Claim", code, pt[2].id, "FullHouse");
        Assert.Equal("won", w3.status); Assert.Equal(3, w3.slot);
        Assert.Equal("won", (await p.InvokeAsync<RoomStateDto>("GetState", code)).mine["FullHouse"]);   // every ticket has won
        // the quota (4) is shared: another player takes the last slot, then the prize is closed
        Assert.Equal("won", (await q.InvokeAsync<ClaimOutcome>("Claim", code, qt.id, "FullHouse")).status);
        Assert.Equal("Completed", (await host.InvokeAsync<RoomStateDto>("GetState", code)).status);
    }

    // ───────────── v1.2: admin-managed sound settings and the new game pages ─────────────

    private static HttpRequestMessage AsUser(string user, string url) =>
        new(HttpMethod.Get, url) { Headers = { { "Cookie", "dev-user=CORP%5C" + user } } };

    [Fact]
    public async Task Sound_settings_are_global_admin_only_and_reach_every_page()
    {
        await Register("admin", "EADM"); await Register("pat", "EPAT");
        var settings = _factory.Services.GetRequiredService<SettingsService>();
        Assert.True(settings.Sound.AllowMute);                                   // defaults until the Admin saves
        await settings.SaveSoundAsync(new SoundSettings(false, "classic", "classic", 35, false), "CORP\admin");
        await settings.LoadAsync();                                              // proves it was persisted, not just cached
        Assert.Equal(35, settings.Sound.Volume); Assert.Equal("classic", settings.Sound.CallerStyle);

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        var html = await (await client.SendAsync(AsUser("pat", "/"))).Content.ReadAsStringAsync();
        Assert.Contains("data-caller=\"classic\"", html); Assert.Contains("data-volume=\"35\"", html);
        Assert.Contains("data-allow-mute=\"no\"", html); Assert.DoesNotContain("id=\"soundBtn\"", html);   // mute button hidden when the Admin disallows it
        Assert.DoesNotContain("id=\"callerStyle\"", html);                      // no per-user caller selector any more

        Assert.NotEqual(System.Net.HttpStatusCode.OK, (await client.SendAsync(AsUser("pat", "/Admin/Sound"))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.SendAsync(AsUser("admin", "/Admin/Sound"))).StatusCode);
    }

    [Fact]
    public async Task Player_and_host_game_pages_render_with_the_tabbed_layout()
    {
        var emps = _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EmployeeService>();
        var hostId = await Register("hpg", "EHPG"); await Register("ppg", "EPPG");
        await emps.SetHostAsync("t", hostId, true);
        var code = await _factory.Services.GetRequiredService<GameService>().CreateRoomAsync(hostId, new CreateRoomRequest("Pages", 10, 2, CallMode.Manual, 6, CutMode.Auto,
            new() { new(PrizePattern.FullHouse, 2, null) }, 3));
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var (user, url, marker) in new[] { ("ppg", "/Rooms/Play/" + code, "id=\"tix\""), ("hpg", "/Rooms/Host/" + code, "id=\"board\"") })
        {
            var res = await client.SendAsync(AsUser(user, url));
            Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
            var html = await res.Content.ReadAsStringAsync();
            Assert.Contains("class=\"panelC\"", html); Assert.Contains("id=\"dock\"", html); Assert.Contains(marker, html);
        }
    }
}
