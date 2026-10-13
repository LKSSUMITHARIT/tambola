namespace Tambola.Web.Services;

/// <summary>Calls the next number for rooms in Auto calling mode. State lives in SQLite, so it resumes after a restart.</summary>
public class AutoCallerService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<AutoCallerService> _log;
    public AutoCallerService(IServiceProvider sp, ILogger<AutoCallerService> log) { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var game = _sp.GetRequiredService<GameService>();
                foreach (var id in await game.DueAutoRoomsAsync())
                    await game.AutoCallAsync(id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Auto caller tick failed");
            }
        }
    }
}
