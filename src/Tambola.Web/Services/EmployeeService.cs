using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tambola.Domain.Entities;
using Tambola.Web.Auth;
using Tambola.Web.Data;

namespace Tambola.Web.Services;

public class EmployeeService
{
    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly AppOptions _opt;

    public EmployeeService(IDbContextFactory<TambolaDb> dbf, IOptions<AppOptions> opt)
    {
        _dbf = dbf; _opt = opt.Value;
    }

    public bool IsAdmin(string windowsUser) =>
        _opt.AdminUsers.Any(a => string.Equals(a, windowsUser, StringComparison.OrdinalIgnoreCase));

    public async Task<Employee?> FindByWindowsUserAsync(string windowsUser)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        return await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.WindowsUser == windowsUser);
    }

    public static string GuessDisplayName(string windowsUser)
    {
        var user = windowsUser.Contains('\\') ? windowsUser[(windowsUser.IndexOf('\\') + 1)..] : windowsUser;
        var parts = user.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }

    /// <summary>Registers an employee. Returns an error message, or null on success.</summary>
    public async Task<string?> RegisterAsync(string windowsUser, string displayName, string code, string email, string? password = null)
    {
        if (!_opt.IsDomainAllowed(windowsUser))
            return "Your domain is not permitted to use this application.";
        if (password != null && PasswordError(password) is { } pe) return pe;
        code = (code ?? "").Trim().ToUpperInvariant();
        email = (email ?? "").Trim().ToLowerInvariant();
        displayName = (displayName ?? "").Trim();

        if (displayName.Length < 2) return "Please enter your name.";
        if (!Regex.IsMatch(code, _opt.EmployeeCodeRegex, RegexOptions.IgnoreCase))
            return "Employee Code format is not valid.";
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return "Please enter a valid email address.";
        if (_opt.AllowedEmailDomains.Length > 0 &&
            !_opt.AllowedEmailDomains.Any(d => email.EndsWith("@" + d.TrimStart('@'), StringComparison.OrdinalIgnoreCase)))
            return "Please use your company email (" + string.Join(", ", _opt.AllowedEmailDomains.Select(d => "@" + d.TrimStart('@'))) + ").";

        await using var db = await _dbf.CreateDbContextAsync();
        if (await db.Employees.AnyAsync(e => e.WindowsUser == windowsUser))
            return "This Windows account is already registered.";
        if (await db.Employees.AnyAsync(e => e.EmployeeCode == code))
            return "This Employee Code is already registered with another email. Contact the Admin if this is a mistake.";
        if (await db.Employees.AnyAsync(e => e.Email == email))
            return "This email is already registered with another Employee Code.";

        db.Employees.Add(new Employee
        {
            WindowsUser = windowsUser, EmployeeCode = code, Email = email, DisplayName = displayName,
            PasswordHash = password == null ? null : PasswordHasher.Hash(password)
        });
        db.AuditLogs.Add(new AuditLog { Actor = windowsUser, Action = "Register", Details = $"{code} / {email}" });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return "Registration conflicts with an existing record."; }
        return null;
    }

    // ── Manual (Linux) mode: password accounts ──

    public string? PasswordError(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < _opt.PasswordMinLength
            ? $"Password must be at least {_opt.PasswordMinLength} characters." : null;

    /// <summary>Builds DOMAIN\userid after validating both parts. Returns an error or null.</summary>
    public string? BuildAccount(string? domain, string? userId, out string account)
    {
        account = "";
        domain = (domain ?? "").Trim(); userId = (userId ?? "").Trim().ToLowerInvariant();
        var allowed = _opt.AllowedDomains.FirstOrDefault(d => string.Equals(d.Trim(), domain, StringComparison.OrdinalIgnoreCase));
        if (allowed == null) return "Please choose a valid domain.";
        if (!Regex.IsMatch(userId, @"^[a-z0-9._\-]{2,64}$")) return "User ID may contain letters, digits, '.', '_' and '-' (2–64 characters).";
        account = allowed.Trim() + "\\" + userId;
        return null;
    }

    /// <summary>Checks the password. Returns the employee on success; null on any failure (unknown user, wrong password, no password set).</summary>
    public async Task<Employee?> VerifyLoginAsync(string account, string password)
    {
        var emp = await FindByWindowsUserAsync(account);
        // run a hash even when the user does not exist so timing does not reveal valid accounts
        var ok = PasswordHasher.Verify(password ?? "", emp?.PasswordHash ?? DummyHash);
        return ok && emp != null ? emp : null;
    }
    private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password");

    public async Task<string?> ChangePasswordAsync(string actor, int employeeId, string newPassword, bool mustChange)
    {
        if (PasswordError(newPassword) is { } err) return err;
        await using var db = await _dbf.CreateDbContextAsync();
        var e = await db.Employees.FindAsync(employeeId) ?? throw new InvalidOperationException("Employee not found");
        e.PasswordHash = PasswordHasher.Hash(newPassword);
        e.MustChangePassword = mustChange;
        db.AuditLogs.Add(new AuditLog { Actor = actor, Action = mustChange ? "ResetPassword" : "ChangePassword", Details = $"{e.DisplayName} ({e.EmployeeCode})" });
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Admin reset: returns a random temporary password the user must change at next login.</summary>
    public async Task<string> ResetPasswordAsync(string actor, int employeeId)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var temp = new string(Enumerable.Range(0, Math.Max(10, _opt.PasswordMinLength)).Select(_ => alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        await ChangePasswordAsync(actor, employeeId, temp, true);
        return temp;
    }

    public async Task SetHostAsync(string actor, int employeeId, bool isHost)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var e = await db.Employees.FindAsync(employeeId) ?? throw new InvalidOperationException("Employee not found");
        e.IsHost = isHost;
        db.AuditLogs.Add(new AuditLog { Actor = actor, Action = isHost ? "GrantHost" : "RevokeHost", Details = $"{e.DisplayName} ({e.EmployeeCode})" });
        await db.SaveChangesAsync();
    }

    public async Task SetActiveAsync(string actor, int employeeId, bool active)
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var e = await db.Employees.FindAsync(employeeId) ?? throw new InvalidOperationException("Employee not found");
        e.IsActive = active;
        db.AuditLogs.Add(new AuditLog { Actor = actor, Action = active ? "ActivateEmployee" : "DeactivateEmployee", Details = $"{e.DisplayName} ({e.EmployeeCode})" });
        await db.SaveChangesAsync();
    }
}
