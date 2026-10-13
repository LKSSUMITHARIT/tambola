namespace Tambola.Web.Services;

public class AppOptions
{
    public const string Section = "Tambola";

    /// <summary>
    /// "Windows" (IIS, Negotiate/Kerberos), "Manual" (Linux: domain + userID + password, own login pages)
    /// or "Dev" (cookie-based fake login for local testing only).
    /// </summary>
    public string AuthMode { get; set; } = "Windows";
    /// <summary>Accounts (DOMAIN\user) that are Admins.</summary>
    public string[] AdminUsers { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Domains whose users may use the app (e.g. CORP). Windows mode: users from any other domain are blocked.
    /// Manual mode: these are the choices in the login/registration dropdown. Empty = any domain (Manual mode then needs at least one).
    /// </summary>
    public string[] AllowedDomains { get; set; } = Array.Empty<string>();
    public int PasswordMinLength { get; set; } = 8;
    /// <summary>True when a reverse proxy (Caddy/nginx) terminates HTTPS in front of the app: trust X-Forwarded-For/-Proto.</summary>
    public bool BehindProxy { get; set; }

    public bool IsManual => AuthMode.Equals("Manual", StringComparison.OrdinalIgnoreCase);
    public bool IsDev => AuthMode.Equals("Dev", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the DOMAIN\user account belongs to an allowed domain (or no restriction is configured).</summary>
    public bool IsDomainAllowed(string windowsUser)
    {
        if (AllowedDomains.Length == 0) return true;
        var i = windowsUser.IndexOf('\\');
        if (i <= 0) return false;                       // no domain part (e.g. a local or UPN-style name) cannot be validated
        var domain = windowsUser[..i];
        return AllowedDomains.Any(d => string.Equals(d.Trim(), domain, StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>Allowed email domains. Empty = any domain.</summary>
    public string[] AllowedEmailDomains { get; set; } = Array.Empty<string>();
    public string EmployeeCodeRegex { get; set; } = @"^[A-Za-z0-9\-]{3,20}$";
    /// <summary>Players may still join/buy tickets while Running until this many numbers were called.</summary>
    public int LateJoinMaxCalls { get; set; } = 10;
    public int DefaultMaxTicketsPerPlayer { get; set; } = 3;
    /// <summary>Theme for users who have not chosen one: "dark", "light" or "system".</summary>
    public string DefaultTheme { get; set; } = "dark";
    /// <summary>Number announcements + sound effects on by default (each user can mute in the header).</summary>
    public bool DefaultSoundOn { get; set; } = true;
}
