using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tambola.Domain.Entities;
using Tambola.Web.Auth;
using Tambola.Web.Services;

namespace Tambola.Web.Pages.Rooms;

public class CreateModel : PageModel
{
    private readonly GameService _game;
    private readonly AppOptions _opt;
    public CreateModel(GameService game, IOptions<AppOptions> opt) { _game = game; _opt = opt.Value; }

    public class PrizeInput
    {
        public PrizePattern Pattern { get; set; }
        public bool Enabled { get; set; }
        public int Winners { get; set; } = 1;
        public string? Reward { get; set; }
    }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public int MaxPlayers { get; set; } = 100;
    [BindProperty] public int MaxTickets { get; set; } = 3;
    /// <summary>Wrong claims a player may make per prize before it locks for them (0 = unlimited).</summary>
    [BindProperty] public int MaxWrongClaims { get; set; } = 3;
    [BindProperty] public CallMode CallMode { get; set; }
    [BindProperty] public int Interval { get; set; } = 6;
    [BindProperty] public CutMode CutMode { get; set; }
    [BindProperty] public List<PrizeInput> Prizes { get; set; } = new();
    public string? Error { get; private set; }

    public void OnGet()
    {
        MaxTickets = _opt.DefaultMaxTicketsPerPlayer;
        Prizes = new()
        {
            new() { Pattern = PrizePattern.FourCorners, Enabled = true, Winners = 4 },
            new() { Pattern = PrizePattern.FirstRow, Enabled = true, Winners = 5 },
            new() { Pattern = PrizePattern.SecondRow, Enabled = true, Winners = 5 },
            new() { Pattern = PrizePattern.ThirdRow, Enabled = true, Winners = 5 },
            new() { Pattern = PrizePattern.FullHouse, Enabled = true, Winners = 1 },
            new() { Pattern = PrizePattern.EarlyFive, Enabled = false, Winners = 1 },
            new() { Pattern = PrizePattern.Star, Enabled = false, Winners = 1 },
            new() { Pattern = PrizePattern.Pyramid, Enabled = false, Winners = 1 },
            new() { Pattern = PrizePattern.Diamond, Enabled = false, Winners = 1 },
            new() { Pattern = PrizePattern.LetterH, Enabled = false, Winners = 1 },
            new() { Pattern = PrizePattern.LetterX, Enabled = false, Winners = 1 },
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var code = await _game.CreateRoomAsync(User.GetEmployeeId(), new CreateRoomRequest(Name, MaxPlayers, MaxTickets, CallMode, Interval, CutMode,
                Prizes.Where(p => p.Enabled).Select(p => new PrizeRequest(p.Pattern, p.Winners, p.Reward)).ToList(), MaxWrongClaims));
            return Redirect("/Rooms/Host/" + code);
        }
        catch (GameException ex) { Error = ex.Message; return Page(); }
    }
}
