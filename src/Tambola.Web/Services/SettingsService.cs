using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tambola.Domain.Entities;
using Tambola.Web.Data;
using Tambola.Web.Hubs;

namespace Tambola.Web.Services;

/// <summary>
/// Caller sound options, set once by the Admin and applied to every user (not per user).
/// CallerStyle: "hindi" (recorded funny clips) | "classic" (browser voice). Fallback (used when a clip is missing): "hindi" | "classic".
/// </summary>
public record SoundSettings(bool Enabled, string CallerStyle, string Fallback, int Volume, bool AllowMute)
{
    public static readonly string[] Styles = { "hindi", "classic" };
    public SoundSettings Normalized() => this with
    {
        CallerStyle = Styles.Contains(CallerStyle) ? CallerStyle : "hindi",
        Fallback = Styles.Contains(Fallback) ? Fallback : "hindi",
        Volume = Math.Clamp(Volume, 0, 100)
    };
}

/// <summary>Reads/writes global settings in the AppSettings table, caches them in memory and pushes changes to connected clients.</summary>
public class SettingsService
{
    private const string SoundKey = "Sound";
    private readonly IDbContextFactory<TambolaDb> _dbf;
    private readonly IHubContext<GameHub> _hub;
    private readonly AppOptions _opt;
    private volatile SoundSettings? _sound;

    public SettingsService(IDbContextFactory<TambolaDb> dbf, IHubContext<GameHub> hub, IOptions<AppOptions> opt)
    { _dbf = dbf; _hub = hub; _opt = opt.Value; }

    /// <summary>Current sound options (falls back to appsettings DefaultSoundOn until the Admin saves once).</summary>
    public SoundSettings Sound => _sound ?? Default();

    private SoundSettings Default() => new(_opt.DefaultSoundOn, "hindi", "hindi", 80, true);

    public async Task LoadAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SoundKey);
        _sound = row == null ? null : Parse(row.Value, Default());
    }

    public async Task SaveSoundAsync(SoundSettings s, string actor)
    {
        s = s.Normalized();
        await using var db = await _dbf.CreateDbContextAsync();
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == SoundKey);
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        if (row == null) db.AppSettings.Add(new AppSetting { Key = SoundKey, Value = json }); else row.Value = json;
        db.AuditLogs.Add(new AuditLog { Actor = actor, Action = "SoundSettings", Details = $"sound={(s.Enabled ? "on" : "off")}, caller={s.CallerStyle}, fallback={s.Fallback}, volume={s.Volume}, mute={(s.AllowMute ? "allowed" : "locked")}" });
        await db.SaveChangesAsync();
        _sound = s;
        await _hub.Clients.All.SendAsync("SettingsChanged", Payload(s));
    }

    /// <summary>Shape sent to browsers (and mirrored by the data-* attributes in _Layout).</summary>
    public static object Payload(SoundSettings s) => new { enabled = s.Enabled, caller = s.CallerStyle, fallback = s.Fallback, volume = s.Volume, allowMute = s.AllowMute };

    private static SoundSettings Parse(string json, SoundSettings fallback)
    {
        try { return (System.Text.Json.JsonSerializer.Deserialize<SoundSettings>(json) ?? fallback).Normalized(); }
        catch (System.Text.Json.JsonException) { return fallback; }
    }
}
