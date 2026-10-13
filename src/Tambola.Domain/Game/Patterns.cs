using Tambola.Domain.Entities;

namespace Tambola.Domain.Game;

/// <summary>Strategy: decides whether a ticket satisfies a winning case given the numbers called so far.</summary>
public interface IWinPattern
{
    PrizePattern Pattern { get; }
    string DisplayName { get; }
    bool IsSatisfied(TicketGrid ticket, ISet<int> called);
}

public abstract class RequiredNumbersPattern : IWinPattern
{
    public abstract PrizePattern Pattern { get; }
    public abstract string DisplayName { get; }
    protected abstract IEnumerable<int> Required(TicketGrid t);
    public bool IsSatisfied(TicketGrid ticket, ISet<int> called) => Required(ticket).All(called.Contains);
}

public sealed class FourCornersPattern : RequiredNumbersPattern
{
    public override PrizePattern Pattern => PrizePattern.FourCorners;
    public override string DisplayName => "Four Corners";
    protected override IEnumerable<int> Required(TicketGrid t)
    {
        var top = t.RowNumbers(0); var bottom = t.RowNumbers(2);
        return new[] { top[0], top[^1], bottom[0], bottom[^1] };
    }
}

public sealed class RowPattern : RequiredNumbersPattern
{
    private readonly int _row;
    public RowPattern(PrizePattern p, int row, string name) { Pattern = p; _row = row; DisplayName = name; }
    public override PrizePattern Pattern { get; }
    public override string DisplayName { get; }
    protected override IEnumerable<int> Required(TicketGrid t) => t.RowNumbers(_row);
}

public sealed class FullHousePattern : RequiredNumbersPattern
{
    public override PrizePattern Pattern => PrizePattern.FullHouse;
    public override string DisplayName => "Full House";
    protected override IEnumerable<int> Required(TicketGrid t) => t.Numbers();
}

public sealed class EarlyFivePattern : IWinPattern
{
    public PrizePattern Pattern => PrizePattern.EarlyFive;
    public string DisplayName => "Early Five";
    public bool IsSatisfied(TicketGrid ticket, ISet<int> called) => ticket.Numbers().Count(called.Contains) >= 5;
}

public static class PatternCatalog
{
    private static readonly Dictionary<PrizePattern, IWinPattern> Map = new IWinPattern[]
    {
        new FourCornersPattern(),
        new RowPattern(PrizePattern.FirstRow, 0, "1st Row"),
        new RowPattern(PrizePattern.SecondRow, 1, "2nd Row"),
        new RowPattern(PrizePattern.ThirdRow, 2, "3rd Row"),
        new FullHousePattern(),
        new EarlyFivePattern(),
    }.ToDictionary(p => p.Pattern);

    public static IWinPattern Get(PrizePattern p) => Map[p];
    public static IEnumerable<IWinPattern> All => Map.Values;
}
