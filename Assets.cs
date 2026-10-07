using System.Security.Cryptography;

namespace IGP_Soft;

internal static class AssetStore
{
    public const string FolderName = "Assets";

    public static string Root => Path.Combine(AppContext.BaseDirectory, FolderName);

    public static event Action<IReadOnlyDictionary<string, string>>? Moved;

    public static bool IsAssetPath(string s) =>
        s.StartsWith(FolderName + "\\", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith(FolderName + "/", StringComparison.OrdinalIgnoreCase);

    public static string Resolve(string s) =>
        IsAssetPath(s) ? Path.Combine(AppContext.BaseDirectory, s.Replace('/', '\\')) : s;

    public static bool IsExternalFile(string s)
    {
        if (s.Contains("://") || IsAssetPath(s)) return false;
        try { return Path.IsPathRooted(s) && File.Exists(s); }
        catch { return false; }
    }

    public static string Import(string path)
    {
        if (!IsExternalFile(path)) return path;
        try
        {
            string full = Path.GetFullPath(path);
            string root = Root + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return Relative(full);

            Directory.CreateDirectory(Root);
            string name = Path.GetFileNameWithoutExtension(full);
            string ext = Path.GetExtension(full);
            string dest = Path.Combine(Root, name + ext);
            for (int n = 2; File.Exists(dest); n++)
            {
                if (SameContent(full, dest)) return Relative(dest);
                dest = Path.Combine(Root, $"{name} ({n}){ext}");
            }
            File.Copy(full, dest);
            return Relative(dest);
        }
        catch { return path; }
    }

    public static Dictionary<string, string> ImportAll(IEnumerable<string> paths)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in paths.Where(IsExternalFile).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string moved = Import(p);
            if (!string.Equals(moved, p, StringComparison.OrdinalIgnoreCase)) map[p] = moved;
        }
        if (map.Count > 0)
        {
            ProfileStore.RewriteUrls(map);
            Moved?.Invoke(map);
        }
        return map;
    }

    public static List<string> ExternalFilesInUse() =>
        LinkStore.Links.Select(l => l.Url)
            .Concat(ProfileStore.Items.SelectMany(p => p.Monitors).SelectMany(m => m.Links).Select(z => z.Url))
            .Where(IsExternalFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static int MoveExternalFilesInUse()
    {
        Dictionary<string, string> map = ImportAll(ExternalFilesInUse());
        if (map.Count > 0)
            LinkStore.Replace(LinkStore.Links.Select(l =>
                map.TryGetValue(l.Url, out string? to) ? l with { Url = to } : l).ToList());
        return map.Count;
    }

    private static string Relative(string full) =>
        Path.Combine(FolderName, Path.GetRelativePath(Root, full));

    private static bool SameContent(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using FileStream fa = File.OpenRead(a), fb = File.OpenRead(b);
            return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
        }
        catch { return false; }
    }
}
