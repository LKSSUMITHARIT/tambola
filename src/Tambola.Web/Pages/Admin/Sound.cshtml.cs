using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Admin;

/// <summary>Global caller sound options: set once by the Admin, applied to every user.</summary>
public class SoundModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly IWebHostEnvironment _env;
    public SoundModel(SettingsService settings, IWebHostEnvironment env) { _settings = settings; _env = env; }

    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public string CallerStyle { get; set; } = "hindi";
    [BindProperty] public string Fallback { get; set; } = "hindi";
    [BindProperty] public int Volume { get; set; } = 80;
    [BindProperty] public bool AllowMute { get; set; } = true;
    public bool Saved { get; private set; }
    /// <summary>Numbers 1-90 that have a recorded clip in wwwroot/audio/hi.</summary>
    public HashSet<int> Clips { get; private set; } = new();

    public void OnGet(bool saved = false)
    {
        var s = _settings.Sound;
        Enabled = s.Enabled; CallerStyle = s.CallerStyle; Fallback = s.Fallback; Volume = s.Volume; AllowMute = s.AllowMute;
        Saved = saved; LoadClips();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _settings.SaveSoundAsync(new SoundSettings(Enabled, CallerStyle, Fallback, Volume, AllowMute), User.Identity!.Name!);
        return RedirectToPage(new { saved = true });
    }

    private void LoadClips()
    {
        var dir = Path.Combine(_env.WebRootPath, "audio", "hi");
        for (var n = 1; n <= 90; n++) if (System.IO.File.Exists(Path.Combine(dir, n + ".mp3"))) Clips.Add(n);
    }
}
