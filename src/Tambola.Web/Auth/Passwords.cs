using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Tambola.Web.Auth;

/// <summary>PBKDF2-SHA256 password hashing. Format: "v1.{iterations}.{saltBase64}.{hashBase64}".</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000, SaltSize = 16, HashSize = 32;

    public static string Hash(string password, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var p = stored.Split('.');
        if (p.Length != 4 || p[0] != "v1" || !int.TryParse(p[1], out var iterations) || iterations < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(p[2]);
            var expected = Convert.FromBase64String(p[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>
/// In-memory brute-force guard: after <see cref="MaxFailures"/> failed logins for the same account+IP the pair is
/// locked out for <see cref="LockFor"/>. A successful login clears the counter.
/// </summary>
public class LoginThrottle
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (int Failures, DateTime LockedUntil)> _state = new();
    private readonly Func<DateTime> _now;

    public LoginThrottle() : this(() => DateTime.UtcNow) { }
    public LoginThrottle(Func<DateTime> now) => _now = now;

    private static string Key(string account, string? ip) => account.ToLowerInvariant() + "|" + ip;

    public bool IsLocked(string account, string? ip, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (!_state.TryGetValue(Key(account, ip), out var s) || s.LockedUntil <= _now()) return false;
        remaining = s.LockedUntil - _now();
        return true;
    }

    public void RecordFailure(string account, string? ip) =>
        _state.AddOrUpdate(Key(account, ip), _ => (1, DateTime.MinValue), (_, s) =>
        {
            // an expired lockout starts a fresh count
            var failures = s.LockedUntil != DateTime.MinValue && s.LockedUntil <= _now() ? 1 : s.Failures + 1;
            return failures >= MaxFailures ? (0, _now() + LockFor) : (failures, DateTime.MinValue);
        });

    public void Clear(string account, string? ip) => _state.TryRemove(Key(account, ip), out _);
}
