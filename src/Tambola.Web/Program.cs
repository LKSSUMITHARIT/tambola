using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Tambola.Web.Auth;
using Tambola.Web.Data;
using Tambola.Web.Hubs;
using Tambola.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/tambola-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
var opts = builder.Configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();

// ── storage: SQLite (WAL) ──
// resolved lazily so configuration overrides (tests, environment variables) are honoured
builder.Services.AddDbContextFactory<TambolaDb>((sp, o) =>
{
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Tambola") ?? "Data Source=App_Data/tambola.db;Default Timeout=30";
    var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource;
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dataSource))!);
    o.UseSqlite(cs).AddInterceptors(new SqlitePragmaInterceptor());
});

// ── authentication: Windows (Negotiate) on IIS, Manual (domain + userID + password, cookie) on Linux, Dev cookie for local testing ──
if (opts.IsDev)
    builder.Services.AddAuthentication(DevAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });
else if (opts.IsManual)
{
    if (opts.AllowedDomains.Length == 0) throw new InvalidOperationException("Tambola:AuthMode=Manual needs at least one entry in Tambola:AllowedDomains.");
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
    {
        o.Cookie.Name = "tambola.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.LoginPath = "/Account/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        // SignalR/API calls must get a status code, not an HTML redirect
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/hubs")) ctx.Response.StatusCode = 401; else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
}
else
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

if (opts.BehindProxy)
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();   // the app only listens on localhost behind the proxy
    });

builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddScoped<IClaimsTransformation, EmployeeClaimsTransformation>();
builder.Services.AddSingleton<IAuthorizationHandler, RegisteredHandler>();
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy(Policies.Registered, p => p.RequireAuthenticatedUser().AddRequirements(new RegisteredRequirement()));
    o.AddPolicy(Policies.Host, p => p.RequireAuthenticatedUser().AddRequirements(new RegisteredRequirement()).RequireRole("Host"));
    o.AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().AddRequirements(new RegisteredRequirement()).RequireRole("Admin"));
});

builder.Services.AddScoped<EmployeeService>();
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService<AutoCallerService>();
builder.Services.AddSignalR();
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizePage("/Index", Policies.Registered);
    o.Conventions.AuthorizePage("/History", Policies.Registered);
    o.Conventions.AuthorizeFolder("/Rooms", Policies.Registered);
    o.Conventions.AuthorizePage("/Rooms/Create", Policies.Host);
    o.Conventions.AuthorizeFolder("/Admin", Policies.Admin);
    o.Conventions.AllowAnonymousToFolder("/Dev");
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Account/Register");
});
// EmployeeService is scoped but GameService is a singleton that only needs the DbContext factory.

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TambolaDb>>();
    await using var db = await dbf.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await SchemaUpgrader.UpgradeAsync(db);   // adds columns introduced after a database was first created
}

if (opts.IsDev)
    app.Logger.LogWarning("AuthMode=Dev: fake cookie login is ENABLED. Never use in production.");
if (opts.IsManual) app.Logger.LogInformation("AuthMode=Manual: domain + userID + password login. Allowed domains: {Domains}", string.Join(", ", opts.AllowedDomains));

if (opts.BehindProxy) app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
// Friendly error pages. Never touch 401: that is the Windows (Negotiate) challenge and re-running
// authentication in the middle of the handshake throws "incomplete authentication context".
app.UseStatusCodePages(ctx =>
{
    var http = ctx.HttpContext;
    if (http.Response.StatusCode != StatusCodes.Status401Unauthorized && !http.Request.Path.StartsWithSegments("/hubs")
        && !http.Request.Path.StartsWithSegments("/Error"))
        http.Response.Redirect("/Error?code=" + http.Response.StatusCode);
    return Task.CompletedTask;
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

// Authenticated but not registered (or deactivated) → registration page; hub/api calls get 403.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    var isStatic = path.StartsWithSegments("/lib") || path.StartsWithSegments("/css") || path.StartsWithSegments("/js");
    if (ctx.User.Identity?.IsAuthenticated == true && !isStatic)
    {
        // domain validator: authenticated but from a domain that is not allowed
        if (ctx.User.HasClaim(c => c.Type == Claims.Blocked))
        {
            if (path.StartsWithSegments("/Blocked") || path.StartsWithSegments("/Account/ChangePassword") || path.StartsWithSegments("/Error")) { await next(); return; }
            if (path.StartsWithSegments("/hubs")) { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Redirect("/Blocked");
            return;
        }
        // an Admin reset the password: nothing else is reachable until a new one is chosen
        if (ctx.User.HasClaim(c => c.Type == Claims.MustChangePassword) &&
            !path.StartsWithSegments("/Account/ChangePassword") && !path.StartsWithSegments("/Error"))
        {
            if (path.StartsWithSegments("/hubs")) { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Redirect("/Account/ChangePassword");
            return;
        }
        var open = path.StartsWithSegments("/Register") || path.StartsWithSegments("/Dev") || path.StartsWithSegments("/Help") ||
                   path.StartsWithSegments("/Error") || path.StartsWithSegments("/Account") || path.StartsWithSegments("/Blocked");
        if (!open && !ctx.User.HasClaim(c => c.Type == Claims.EmployeeId))
        {
            if (path.StartsWithSegments("/hubs")) { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Redirect(opts.IsManual ? "/Account/Register" : "/Register");
            return;
        }
    }
    await next();
});

app.UseAuthorization();
app.MapRazorPages();
app.MapHub<GameHub>("/hubs/game");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow })).AllowAnonymous();
app.Run();

public partial class Program { }
