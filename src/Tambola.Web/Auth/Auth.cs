using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Tambola.Web.Services;

namespace Tambola.Web.Auth;

public static class Policies
{
    public const string Registered = "Registered";
    public const string Host = "Host";
    public const string Admin = "Admin";
}

public static class Claims
{
    public const string EmployeeId = "tambola:emp";
    public const string DisplayName = "tambola:name";
    /// <summary>Authenticated, but their domain is not in AllowedDomains.</summary>
    public const string Blocked = "tambola:blocked";
    /// <summary>Manual mode: an Admin reset the password, so the user must set a new one first.</summary>
    public const string MustChangePassword = "tambola:mustchange";
    public static int GetEmployeeId(this ClaimsPrincipal u) => int.Parse(u.FindFirstValue(EmployeeId)!);
    public static string GetDisplayName(this ClaimsPrincipal u) => u.FindFirstValue(DisplayName) ?? u.Identity?.Name ?? "";
}

/// <summary>Adds employee id / Host / Admin claims for registered, active employees.</summary>
public class EmployeeClaimsTransformation : IClaimsTransformation
{
    private readonly EmployeeService _emps;
    private readonly AppOptions _opt;
    public EmployeeClaimsTransformation(EmployeeService emps, IOptions<AppOptions> opt) { _emps = emps; _opt = opt.Value; }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == Claims.EmployeeId || c.Type == Claims.Blocked)) return principal;
        var name = principal.Identity.Name;
        if (string.IsNullOrEmpty(name)) return principal;

        var clone = new ClaimsPrincipal(principal);
        var id = new ClaimsIdentity();
        // Domain validator: users from a domain that is not allowed get no roles and no employee claim at all.
        if (!_opt.IsDomainAllowed(name))
        {
            id.AddClaim(new Claim(Claims.Blocked, name.Contains('\\') ? name[..name.IndexOf('\\')] : "(none)"));
            clone.AddIdentity(id);
            return clone;
        }
        if (_emps.IsAdmin(name)) id.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        var emp = await _emps.FindByWindowsUserAsync(name);
        if (emp is { IsActive: true })
        {
            id.AddClaim(new Claim(Claims.EmployeeId, emp.Id.ToString()));
            id.AddClaim(new Claim(Claims.DisplayName, emp.DisplayName));
            if (emp.IsHost) id.AddClaim(new Claim(ClaimTypes.Role, "Host"));
            if (emp.MustChangePassword) id.AddClaim(new Claim(Claims.MustChangePassword, "1"));
        }
        clone.AddIdentity(id);
        return clone;
    }
}

/// <summary>DEV ONLY: cookie "dev-user" = DOMAIN\user. Lets you test several users without Active Directory.</summary>
public class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAuth";
    public const string Cookie = "dev-user";
    public DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : base(o, l, e) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(Cookie, out var user) || string.IsNullOrWhiteSpace(user))
            return Task.FromResult(AuthenticateResult.NoResult());
        var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user) }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(id), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Request.Path.StartsWithSegments("/hubs")) { Response.StatusCode = 401; return Task.CompletedTask; }
        Response.Redirect("/Dev/Login?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString));
        return Task.CompletedTask;
    }
}

/// <summary>Registered-employee gate: unregistered users are sent to /Register.</summary>
public class RegisteredRequirement : IAuthorizationRequirement { }
public class RegisteredHandler : AuthorizationHandler<RegisteredRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext ctx, RegisteredRequirement req)
    {
        if (ctx.User.HasClaim(c => c.Type == Claims.EmployeeId)) ctx.Succeed(req);
        return Task.CompletedTask;
    }
}
