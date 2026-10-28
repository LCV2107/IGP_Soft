using System.IO.Compression;

namespace IGP_Soft;

internal sealed record ProjectFile(string FullPath, string RelativePath);

internal sealed record TransferResult(int Copied, int Skipped, string Destination);

internal static class ProjectFiles
{
    public const string SessionsFolder = "IGP_Soft_Datos";
    public const string BackupsFolder = "Copias de seguridad";
    private const string RuntimesFolder = "runtimes";

    private static readonly string[] DataFiles =
        { "IGP_Soft.enlaces.txt", "IGP_Soft.perfiles.json", "IGP_Soft.config.txt" };

    private static readonly string[] SkippedProgramExtensions = { ".pdb", ".tmp", ".zip", ".borrar" };

    private static readonly string[] StoredExtensions =
        { ".mp4", ".webm", ".ogv", ".ogg", ".mov", ".m4v", ".mkv", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".jfif", ".zip" };

    private static string Root => AppPaths.DataDir;

    private static string ProgramRoot => AppPaths.ProgramDir;

    public static List<ProjectFile> Collect(bool includeProgram, bool includeSessions)
    {
        var list = new List<ProjectFile>();
        foreach (string name in DataFiles)
        {
            string full = Path.Combine(Root, name);
            if (File.Exists(full)) list.Add(new ProjectFile(full, name));
        }
        AddFolder(list, AssetStore.FolderName);
        if (includeSessions) AddFolder(list, SessionsFolder);

        if (includeProgram)
        {
            foreach (string full in Directory.EnumerateFiles(ProgramRoot))
            {
                string name = Path.GetFileName(full);
                if (DataFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                if (name.Equals(AppPaths.InstalledMarker, StringComparison.OrdinalIgnoreCase)) continue;
                if (SkippedProgramExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
                if (name.Equals("IGP_Soft.anterior.exe", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new ProjectFile(full, name));
            }
            AddFolder(list, RuntimesFolder, ProgramRoot);
        }
        return list;
    }

    public static bool IsInsideProgramFolder(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(ProgramRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string NewFolder(string parent, string baseName)
    {
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        string dir = Path.Combine(parent, $"{baseName}_{stamp}");
        for (int n = 2; Directory.Exists(dir) || File.Exists(dir); n++)
            dir = Path.Combine(parent, $"{baseName}_{stamp} ({n})");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string DefaultZipName() => $"IGP_Soft_{DateTime.Now:yyyy-MM-dd_HHmm}.zip";

    public static TransferResult CopyTo(List<ProjectFile> files, string destination, IProgress<int>? progress)
    {
        int copied = 0, skipped = 0;
        foreach (ProjectFile f in files)
        {
            try
            {
                string target = Path.Combine(destination, f.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using FileStream src = OpenShared(f.FullPath);
                using FileStream dst = new(target, FileMode.CreateNew, FileAccess.Write);
                src.CopyTo(dst);
                File.SetLastWriteTime(target, File.GetLastWriteTime(f.FullPath));
                copied++;
            }
            catch { skipped++; }
            progress?.Report(copied + skipped);
        }
        return new TransferResult(copied, skipped, destination);
    }

    public static TransferResult ZipTo(List<ProjectFile> files, string zipPath, IProgress<int>? progress)
    {
        int copied = 0, skipped = 0;
        string tmp = zipPath + ".tmp";
        using (var zip = new ZipArchive(new FileStream(tmp, FileMode.Create, FileAccess.Write), ZipArchiveMode.Create))
        {
            foreach (ProjectFile f in files)
            {
                try
                {
                    using FileStream src = OpenShared(f.FullPath);
                    CompressionLevel level = StoredExtensions.Contains(Path.GetExtension(f.FullPath), StringComparer.OrdinalIgnoreCase)
                        ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                    ZipArchiveEntry entry = zip.CreateEntry(f.RelativePath.Replace('\\', '/'), level);
                    entry.LastWriteTime = File.GetLastWriteTime(f.FullPath);
                    using Stream dst = entry.Open();
                    src.CopyTo(dst);
                    copied++;
                }
                catch { skipped++; }
                progress?.Report(copied + skipped);
            }
        }
        File.Move(tmp, zipPath, overwrite: true);
        return new TransferResult(copied, skipped, zipPath);
    }

    public static TransferResult BackupBeforeRestore()
    {
        string parent = Path.Combine(Root, BackupsFolder);
        Directory.CreateDirectory(parent);
        string dest = NewFolder(parent, "Antes de restaurar");
        return CopyTo(Collect(includeProgram: false, includeSessions: true), dest, null);
    }

    public static TransferResult RestoreFromZip(string zipPath, IProgress<int>? progress)
    {
        int copied = 0, skipped = 0;
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry e in zip.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (!IsRestorable(rel)) continue;
            try
            {
                string target = SafeTarget(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, overwrite: true);
                copied++;
            }
            catch { skipped++; }
            progress?.Report(copied + skipped);
        }
        return new TransferResult(copied, skipped, Root);
    }

    public static TransferResult RestoreFromFolder(string folder, IProgress<int>? progress)
    {
        int copied = 0, skipped = 0;
        string source = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        foreach (string full in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(source, full);
            if (!IsRestorable(rel)) continue;
            try
            {
                string target = SafeTarget(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(full, target, overwrite: true);
                copied++;
            }
            catch { skipped++; }
            progress?.Report(copied + skipped);
        }
        return new TransferResult(copied, skipped, Root);
    }

    public static int CountRestorable(string source)
    {
        if (File.Exists(source))
        {
            using ZipArchive zip = ZipFile.OpenRead(source);
            return zip.Entries.Count(e => !e.FullName.EndsWith('/') &&
                                          IsRestorable(e.FullName.Replace('/', Path.DirectorySeparatorChar)));
        }
        string root = Path.GetFullPath(source);
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                        .Count(f => IsRestorable(Path.GetRelativePath(root, f)));
    }

    private static bool IsRestorable(string rel)
    {
        if (DataFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)) return true;
        string first = rel.Split(Path.DirectorySeparatorChar)[0];
        return first.Equals(AssetStore.FolderName, StringComparison.OrdinalIgnoreCase) ||
               first.Equals(SessionsFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeTarget(string rel)
    {
        string target = Path.GetFullPath(Path.Combine(Root, rel));
        if (!target.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Ruta fuera de la carpeta del programa.");
        return target;
    }

    private static void AddFolder(List<ProjectFile> list, string folder, string? root = null)
    {
        root ??= Root;
        string dir = Path.Combine(root, folder);
        if (!Directory.Exists(dir)) return;
        foreach (string full in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            list.Add(new ProjectFile(full, Path.GetRelativePath(root, full)));
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
