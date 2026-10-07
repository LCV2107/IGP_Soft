namespace IGP_Soft;

internal sealed record ZoneSpec(int C, int R, int CS = 1, int RS = 1, bool Off = false)
{
    public bool Contains(int c, int r) => c >= C && c < C + CS && r >= R && r < R + RS;
}

internal sealed class GridLayout
{
    public const int Max = 4;

    public int Cols { get; }
    public int Rows { get; }
    public IReadOnlyList<ZoneSpec> Zones { get; }

    private GridLayout(int cols, int rows, IEnumerable<ZoneSpec> zones)
    {
        Cols = cols;
        Rows = rows;
        Zones = zones.OrderBy(z => z.R).ThenBy(z => z.C).ToList();
    }

    public static GridLayout Uniform(int cols, int rows)
    {
        cols = Math.Clamp(cols, 1, Max);
        rows = Math.Clamp(rows, 1, Max);
        var zones = new List<ZoneSpec>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                zones.Add(new ZoneSpec(c, r));
        return new GridLayout(cols, rows, zones);
    }

    public static GridLayout? TryCreate(int cols, int rows, IEnumerable<ZoneSpec>? zones)
    {
        if (cols < 1 || rows < 1 || cols > Max || rows > Max || zones is null) return null;
        var list = zones.ToList();
        var covered = new bool[cols, rows];
        foreach (ZoneSpec z in list)
        {
            if (z.CS < 1 || z.RS < 1 || z.C < 0 || z.R < 0 || z.C + z.CS > cols || z.R + z.RS > rows) return null;
            for (int c = z.C; c < z.C + z.CS; c++)
                for (int r = z.R; r < z.R + z.RS; r++)
                {
                    if (covered[c, r]) return null;
                    covered[c, r] = true;
                }
        }
        foreach (bool b in covered) if (!b) return null;
        return new GridLayout(cols, rows, list);
    }

    public static GridLayout FromSaved(int cols, int rows, IEnumerable<ZoneSpec>? zones) =>
        TryCreate(cols, rows, zones) ?? Uniform(cols, rows);

    public GridLayout With(IEnumerable<ZoneSpec> zones) => TryCreate(Cols, Rows, zones) ?? this;

    public int ZoneAt(int c, int r)
    {
        for (int i = 0; i < Zones.Count; i++)
            if (Zones[i].Contains(c, r)) return i;
        return -1;
    }

    public bool IsUniform => Zones.All(z => z.CS == 1 && z.RS == 1 && !z.Off);

    public int ActiveCount => Zones.Count(z => !z.Off);

    public string Describe() => IsUniform ? $"{Cols}×{Rows}" : $"{ActiveCount} zonas";

    public bool SameAs(GridLayout other) =>
        Cols == other.Cols && Rows == other.Rows && Zones.SequenceEqual(other.Zones);

    public Rectangle AreaOf(ZoneSpec z, Rectangle bounds)
    {
        int x0 = bounds.X + bounds.Width * z.C / Cols;
        int x1 = bounds.X + bounds.Width * (z.C + z.CS) / Cols;
        int y0 = bounds.Y + bounds.Height * z.R / Rows;
        int y1 = bounds.Y + bounds.Height * (z.R + z.RS) / Rows;
        return Rectangle.FromLTRB(x0, y0, x1, y1);
    }

    public RectangleF AreaOf(ZoneSpec z, SizeF size)
    {
        float cw = size.Width / Cols, ch = size.Height / Rows;
        return new RectangleF(z.C * cw, z.R * ch, z.CS * cw, z.RS * ch);
    }

    public static Dictionary<int, int> Carry(GridLayout from, IEnumerable<int> occupied, GridLayout to)
    {
        var result = new Dictionary<int, int>();
        var taken = new HashSet<int>();
        foreach (int oldIndex in occupied.OrderBy(i => i))
        {
            ZoneSpec z = from.Zones[oldIndex];
            if (z.C >= to.Cols || z.R >= to.Rows) continue;
            int target = to.ZoneAt(z.C, z.R);
            if (target < 0 || to.Zones[target].Off || !taken.Add(target)) continue;
            result[oldIndex] = target;
        }
        return result;
    }

    public static IReadOnlyList<(string Name, GridLayout Layout)> Presets { get; } = new List<(string, GridLayout)>
    {
        ("Grande a la izquierda + 2", TryCreate(2, 2, new[] { new ZoneSpec(0, 0, 1, 2), new ZoneSpec(1, 0), new ZoneSpec(1, 1) })!),
        ("Grande arriba + 3", TryCreate(3, 2, new[] { new ZoneSpec(0, 0, 3, 1), new ZoneSpec(0, 1), new ZoneSpec(1, 1), new ZoneSpec(2, 1) })!),
        ("Grande + 5", TryCreate(3, 3, new[]
        {
            new ZoneSpec(0, 0, 2, 2), new ZoneSpec(2, 0), new ZoneSpec(2, 1),
            new ZoneSpec(0, 2), new ZoneSpec(1, 2), new ZoneSpec(2, 2),
        })!),
        ("Grande + 7", TryCreate(4, 4, new[]
        {
            new ZoneSpec(0, 0, 3, 3), new ZoneSpec(3, 0), new ZoneSpec(3, 1), new ZoneSpec(3, 2),
            new ZoneSpec(0, 3), new ZoneSpec(1, 3), new ZoneSpec(2, 3), new ZoneSpec(3, 3),
        })!),
        ("Grande + 3", TryCreate(4, 2, new[]
        {
            new ZoneSpec(0, 0, 2, 2), new ZoneSpec(2, 0, 2, 1), new ZoneSpec(2, 1), new ZoneSpec(3, 1),
        })!),
    };
}
