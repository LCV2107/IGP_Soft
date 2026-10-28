using System.Text;

namespace IGP_Soft;

internal static class AppPaths
{
    public const string InstalledMarker = "IGP_Soft.instalado";

    public static string ProgramDir { get; } = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static bool IsInstalled { get; } = File.Exists(Path.Combine(ProgramDir, InstalledMarker));

    public static string DataDir { get; } = CreateDataDir();

    private static string CreateDataDir()
    {
        if (!IsInstalled) return ProgramDir;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IGP_Soft");
        try { Directory.CreateDirectory(dir); }
        catch { }
        return dir;
    }
}

internal static class Config
{
    public static readonly string FilePath = Path.Combine(AppPaths.DataDir, "IGP_Soft.config.txt");

    private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) Values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }
        catch { }
    }

    public static void Reload()
    {
        Values.Clear();
        Load();
    }

    public static string? Get(string key) => Values.TryGetValue(key, out string? v) ? v : null;

    public static void Set(string key, string value)
    {
        Values[key] = value;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# IGP_Soft — configuración (se edita solo desde el programa).");
            foreach (KeyValuePair<string, string> kv in Values)
                sb.AppendLine($"{kv.Key}={kv.Value}");
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch { }
    }
}
