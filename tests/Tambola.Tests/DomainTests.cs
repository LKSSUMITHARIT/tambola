using Tambola.Domain.Entities;
using Tambola.Domain.Game;

namespace Tambola.Tests;

public class TicketGeneratorTests
{
    [Fact]
    public void Generated_tickets_are_always_valid()
    {
        for (var i = 0; i < 10_000; i++)
            Assert.True(TicketGenerator.IsValid(TicketGenerator.Generate()), $"invalid ticket on iteration {i}");
    }

    [Fact]
    public void Serialize_roundtrip()
    {
        var t = TicketGenerator.Generate();
        var back = TicketGrid.Parse(t.Serialize());
        Assert.Equal(t.Numbers(), back.Numbers());
    }

    [Fact]
    public void Draw_order_is_permutation_of_1_to_90()
    {
        var o = TicketGenerator.NewDrawOrder();
        Assert.Equal(Enumerable.Range(1, 90), o.OrderBy(x => x));
    }
}

public class PatternTests
{
    private static TicketGrid Sample() => TicketGrid.Parse(
        "5,0,21,0,43,0,61,74,0," + "0,14,27,0,48,55,0,0,83," + "9,18,0,36,0,0,68,0,90");

    [Fact]
    public void Sample_ticket_is_valid() => Assert.True(TicketGenerator.IsValid(Sample()));

    [Fact]
    public void Four_corners_needs_first_and_last_of_top_and_bottom_rows()
    {
        var p = PatternCatalog.Get(PrizePattern.FourCorners);
        Assert.False(p.IsSatisfied(Sample(), new HashSet<int> { 5, 74, 9 }));
        Assert.True(p.IsSatisfied(Sample(), new HashSet<int> { 5, 74, 9, 90 }));
    }

    [Theory]
    [InlineData(PrizePattern.FirstRow, new[] { 5, 21, 43, 61, 74 })]
    [InlineData(PrizePattern.SecondRow, new[] { 14, 27, 48, 55, 83 })]
    [InlineData(PrizePattern.ThirdRow, new[] { 9, 18, 36, 68, 90 })]
    public void Row_patterns(PrizePattern pattern, int[] nums)
    {
        var p = PatternCatalog.Get(pattern);
        Assert.False(p.IsSatisfied(Sample(), nums.Skip(1).ToHashSet()));
        Assert.True(p.IsSatisfied(Sample(), nums.ToHashSet()));
    }

    [Fact]
    public void Full_house_needs_all_15()
    {
        var all = Sample().Numbers().ToHashSet();
        var p = PatternCatalog.Get(PrizePattern.FullHouse);
        Assert.True(p.IsSatisfied(Sample(), all));
        all.Remove(5);
        Assert.False(p.IsSatisfied(Sample(), all));
    }

    [Fact]
    public void Early_five_needs_any_five()
    {
        var p = PatternCatalog.Get(PrizePattern.EarlyFive);
        Assert.False(p.IsSatisfied(Sample(), new HashSet<int> { 5, 21, 43, 61 }));
        Assert.True(p.IsSatisfied(Sample(), new HashSet<int> { 5, 21, 43, 61, 90 }));
    }
}
