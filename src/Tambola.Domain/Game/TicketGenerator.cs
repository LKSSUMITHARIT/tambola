using System.Security.Cryptography;

namespace Tambola.Domain.Game;

/// <summary>A 3x9 Tambola ticket. Cell value 0 means blank.</summary>
public sealed class TicketGrid
{
    public const int Rows = 3, Cols = 9;
    public int[,] Cells { get; }
    public TicketGrid(int[,] cells) => Cells = cells;

    public IEnumerable<int> Numbers()
    {
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                if (Cells[r, c] != 0) yield return Cells[r, c];
    }

    public IReadOnlyList<int> RowNumbers(int row)
    {
        var list = new List<int>();
        for (var c = 0; c < Cols; c++) if (Cells[row, c] != 0) list.Add(Cells[row, c]);
        return list;
    }

    public string Serialize()
    {
        var parts = new List<string>(27);
        for (var r = 0; r < Rows; r++) for (var c = 0; c < Cols; c++) parts.Add(Cells[r, c].ToString());
        return string.Join(',', parts);
    }

    public static TicketGrid Parse(string s)
    {
        var v = s.Split(',').Select(int.Parse).ToArray();
        if (v.Length != 27) throw new FormatException("Ticket grid must have 27 values.");
        var cells = new int[Rows, Cols];
        for (var i = 0; i < 27; i++) cells[i / Cols, i % Cols] = v[i];
        return new TicketGrid(cells);
    }
}

/// <summary>Generates valid Indian-style Tambola tickets using a CSPRNG.</summary>
public static class TicketGenerator
{
    public static int NextInt(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);

    public static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Fisher-Yates permutation of 1..90.</summary>
    public static int[] NewDrawOrder()
    {
        var a = Enumerable.Range(1, 90).ToList();
        Shuffle(a);
        return a.ToArray();
    }

    public static TicketGrid Generate()
    {
        while (true)
        {
            // column counts: each column >=1, <=3, total 15
            var counts = Enumerable.Repeat(1, 9).ToArray();
            for (var extra = 6; extra > 0;)
            {
                var c = NextInt(9);
                if (counts[c] < 3) { counts[c]++; extra--; }
            }

            // choose which rows each column occupies so every row ends up with 5 cells
            var occupied = new bool[3, 9];
            var rowTotals = new int[3];
            var ok = true;
            // place 3-count columns first, then 2, then 1, picking rows with room (<5) at random
            foreach (var c in Enumerable.Range(0, 9).OrderByDescending(i => counts[i]).ThenBy(_ => NextInt(1000)))
            {
                var rows = new List<int> { 0, 1, 2 };
                Shuffle(rows);
                rows = rows.OrderByDescending(r => rowTotals[r] >= 5 ? -1 : 5 - rowTotals[r] + NextInt(2)).ToList();
                var placed = 0;
                foreach (var r in rows)
                {
                    if (placed == counts[c]) break;
                    if (rowTotals[r] >= 5) continue;
                    occupied[r, c] = true; rowTotals[r]++; placed++;
                }
                if (placed != counts[c]) { ok = false; break; }
            }
            if (!ok || rowTotals.Any(t => t != 5)) continue;

            var cells = new int[3, 9];
            for (var c = 0; c < 9; c++)
            {
                var lo = c == 0 ? 1 : c * 10;
                var hi = c == 8 ? 90 : c * 10 + 9;
                var pool = Enumerable.Range(lo, hi - lo + 1).ToList();
                Shuffle(pool);
                var picks = pool.Take(counts[c]).OrderBy(x => x).ToList();
                var k = 0;
                for (var r = 0; r < 3; r++) if (occupied[r, c]) cells[r, c] = picks[k++];
            }
            return new TicketGrid(cells);
        }
    }

    /// <summary>Checks the structural rules of a ticket.</summary>
    public static bool IsValid(TicketGrid t)
    {
        var all = new HashSet<int>();
        for (var r = 0; r < 3; r++) if (t.RowNumbers(r).Count != 5) return false;
        for (var c = 0; c < 9; c++)
        {
            var lo = c == 0 ? 1 : c * 10;
            var hi = c == 8 ? 90 : c * 10 + 9;
            var prev = 0; var n = 0;
            for (var r = 0; r < 3; r++)
            {
                var v = t.Cells[r, c];
                if (v == 0) continue;
                if (v < lo || v > hi || v <= prev || !all.Add(v)) return false;
                prev = v; n++;
            }
            if (n == 0) return false;
        }
        return all.Count == 15;
    }
}
