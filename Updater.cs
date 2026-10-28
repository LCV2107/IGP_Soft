using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IGP_Soft;

internal sealed record SemVer(int Major, int Minor, int Patch, string[] Pre) : IComparable<SemVer>
{
    public bool IsPrerelease => Pre.Length > 0;

    public static SemVer? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string s = text.Trim().TrimStart('v', 'V');
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string[] pre = Array.Empty<string>();
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
            s = s[..dash];
        }
        string[] p = s.Split('.');
        if (p.Length < 2 || !int.TryParse(p[0], out int ma) || !int.TryParse(p[1], out int mi)) return null;
        int pa = p.Length > 2 && int.TryParse(p[2], out int x) ? x : 0;
        return new SemVer(ma, mi, pa, pre);
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Pre.Length == 0 || other.Pre.Length == 0) return other.Pre.Length.CompareTo(Pre.Length);
        for (int i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++)
        {
            bool an = int.TryParse(Pre[i], out int a), bn = int.TryParse(other.Pre[i], out int b);
            c = an && bn ? a.CompareTo(b) : an ? -1 : bn ? 1 : string.CompareOrdinal(Pre[i], other.Pre[i]);
            if (c != 0) return c;
        }
        return Pre.Length.CompareTo(other.Pre.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre.Length > 0 ? "-" + string.Join(".", Pre) : "");
}

internal sealed record UpdateInfo(SemVer Version, string Title, string Notes, DateTime Published, string PageUrl,
    string FileName, string FileUrl, long FileSize, string? ShaUrl);

internal enum UpdateChannel { Stable, Experimental }

internal static class Updater
{
    public const string DefaultRepo = "LCV2107/IGP_Soft";
    private const string TmpName = "IGP_Soft.update.tmp";
    private const string OldName = "IGP_Soft.anterior.exe";

    private static readonly HttpClient Http = CreateClient();

    private static string? _pendingInstaller;

    public static event Action<UpdateInfo>? Found;

    public static UpdateInfo? Available { get; private set; }

    public static SemVer Current { get; } =
        SemVer.Parse(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new SemVer(0, 0, 0, Array.Empty<string>());

    public static string Repo => Config.Get("actualizar.repo") is { Length: > 0 } r ? r : DefaultRepo;

    public static UpdateChannel Channel
    {
        get => Config.Get("actualizar.canal") == "experimental" ? UpdateChannel.Experimental : UpdateChannel.Stable;
        set => Config.Set("actualizar.canal", value == UpdateChannel.Experimental ? "experimental" : "estable");
    }

    public static bool Notify
    {
        get => Config.Get("actualizar.avisar") != "0";
        set => Config.Set("actualizar.avisar", value ? "1" : "0");
    }

    public static string? Dismissed
    {
        get => Config.Get("actualizar.omitir");
        set => Config.Set("actualizar.omitir", value ?? "");
    }

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "IGP_Soft.exe");
    private static string ExeDir => Path.GetDirectoryName(ExePath)!;

    public static bool IsPortableBuild =>
        !AppPaths.IsInstalled &&
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "IGP_Soft.dll")) &&
        Path.GetExtension(ExePath).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    public static bool CanSelfUpdate => AppPaths.IsInstalled || (IsPortableBuild && CanWriteHere());

    public static bool CanWriteHere()
    {
        try
        {
            string probe = Path.Combine(ExeDir, $".igp_prueba_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static string? PreviousVersion
    {
        get
        {
            if (AppPaths.IsInstalled) return null;
            string old = Path.Combine(ExeDir, OldName);
            if (!File.Exists(old)) return null;
            try { return FileVersionInfo.GetVersionInfo(old).ProductVersion is { Length: > 0 } v ? SemVer.Parse(v)?.ToString() ?? v : "anterior"; }
            catch { return "anterior"; }
        }
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("IGP_Soft-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static async Task<UpdateInfo?> CheckAsync(bool raiseEvent)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases?per_page=30", cts.Token);
        UpdateInfo? best = ParseReleases(json, Channel, Current, AppPaths.IsInstalled);
        Available = best;
        if (best is not null && raiseEvent) Found?.Invoke(best);
        return best;
    }

    public static UpdateInfo? ParseReleases(string json, UpdateChannel channel, SemVer current, bool installed)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        UpdateInfo? best = null;
        foreach (JsonElement r in doc.RootElement.EnumerateArray())
        {
            if (r.GetProperty("draft").GetBoolean()) continue;
            bool pre = r.GetProperty("prerelease").GetBoolean();
            if (pre && channel == UpdateChannel.Stable) continue;
            SemVer? v = SemVer.Parse(r.GetProperty("tag_name").GetString());
            if (v is null || v.CompareTo(current) <= 0) continue;
            if (best is not null && v.CompareTo(best.Version) <= 0) continue;

            string wanted = installed ? ".msi" : ".exe";
            string? fileName = null, fileUrl = null;
            long size = 0;
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement a in r.GetProperty("assets").EnumerateArray())
            {
                string name = Path.GetFileName(a.GetProperty("name").GetString() ?? "");
                string url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (!IsTrustedUrl(url)) continue;
                if (name.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    fileName = name; fileUrl = url; size = a.GetProperty("size").GetInt64();
                }
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) hashes[name[..^".sha256".Length]] = url;
            }
            if (fileName is null || fileUrl is null) continue;
            string? shaUrl = hashes.TryGetValue(fileName, out string? h) ? h : null;

            string title = r.TryGetProperty("name", out JsonElement n) && n.GetString() is { Length: > 0 } t ? t : $"IGP_Soft {v}";
            string notes = r.TryGetProperty("body", out JsonElement b) ? b.GetString() ?? "" : "";
            DateTime published = r.TryGetProperty("published_at", out JsonElement d) && d.TryGetDateTime(out DateTime dt) ? dt.ToLocalTime() : DateTime.Now;
            best = new UpdateInfo(v, title, notes.Trim(), published, r.GetProperty("html_url").GetString() ?? "",
                fileName, fileUrl, size, shaUrl);
        }
        return best;
    }

    private static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public static async Task InstallAsync(UpdateInfo info, IProgress<(long done, long total)> progress)
    {
        if (AppPaths.IsInstalled)
        {
            string folder = Path.Combine(Path.GetTempPath(), $"IGP_Soft_actualizacion_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            string installer = Path.Combine(folder, Path.GetFileName(info.FileName));
            await DownloadVerifiedAsync(info, installer, progress);
            BackupData("Antes de actualizar");
            _pendingInstaller = installer;
            return;
        }
        if (!IsPortableBuild) throw new InvalidOperationException("Esta copia no es la versión portable de un solo archivo.");
        if (!CanWriteHere()) throw new UnauthorizedAccessException("No se puede escribir en la carpeta del programa.");

        string tmp = Path.Combine(ExeDir, TmpName);
        await DownloadVerifiedAsync(info, tmp, progress);
        BackupData("Antes de actualizar");
        Swap(tmp);
    }

    public static async Task DownloadVerifiedAsync(UpdateInfo info, string tmp, IProgress<(long done, long total)> progress)
    {
        using (HttpResponseMessage resp = await Http.GetAsync(info.FileUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.FileSize;
            await using Stream src = await resp.Content.ReadAsStreamAsync();
            await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                progress.Report((done, total));
            }
        }

        if (info.ShaUrl is null)
        {
            File.Delete(tmp);
            throw new InvalidDataException("La versión publicada no trae su huella de verificación (.sha256).");
        }
        string expected = (await Http.GetStringAsync(info.ShaUrl)).Trim().Split(' ', '\t', '\n')[0].ToLowerInvariant();
        string actual;
        await using (FileStream fs = File.OpenRead(tmp))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(tmp);
            throw new InvalidDataException("La descarga no coincide con la huella publicada. No se instaló nada.");
        }
    }

    public static void Rollback()
    {
        string old = Path.Combine(ExeDir, OldName);
        if (!File.Exists(old)) throw new FileNotFoundException("No hay una versión anterior guardada.");
        BackupData("Antes de volver atrás");
        string tmp = Path.Combine(ExeDir, TmpName);
        File.Copy(old, tmp, overwrite: true);
        Swap(tmp);
    }

    private static void Swap(string newExe)
    {
        string exe = ExePath;
        string old = Path.Combine(ExeDir, OldName);
        if (File.Exists(old))
        {
            try { File.Delete(old); }
            catch { File.Move(old, Path.Combine(ExeDir, $"IGP_Soft.{Guid.NewGuid():N}.borrar")); }
        }
        File.Move(exe, old);
        try { File.Move(newExe, exe); }
        catch
        {
            File.Move(old, exe);
            throw;
        }
    }

    private static void BackupData(string label)
    {
        string parent = Path.Combine(AppPaths.DataDir, ProjectFiles.BackupsFolder);
        Directory.CreateDirectory(parent);
        ProjectFiles.CopyTo(ProjectFiles.Collect(includeProgram: false, includeSessions: false),
            ProjectFiles.NewFolder(parent, $"{label} {Current}"), null);
    }

    public static void Restart(string args)
    {
        if (_pendingInstaller is { } msi)
        {
            RunInstallerAndRestart(msi, args);
            return;
        }
        Process.Start(new ProcessStartInfo(ExePath, $"--esperar={Environment.ProcessId} {args}")
        {
            UseShellExecute = false, WorkingDirectory = ExeDir,
        });
    }

    private static void RunInstallerAndRestart(string msi, string args)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        string msiArgs = $"/i \"{msi}\" /passive /norestart INSTALLFOLDER=\"{ExeDir}\"";
        string script =
            $"$p = Get-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; if ($p) {{ $p.WaitForExit(30000) | Out-Null }}; " +
            $"Start-Process -FilePath 'msiexec.exe' -ArgumentList {Q(msiArgs)} -Wait; " +
            $"Remove-Item -LiteralPath {Q(Path.GetDirectoryName(msi)!)} -Recurse -Force -ErrorAction SilentlyContinue; " +
            $"Start-Process -FilePath {Q(ExePath)} -ArgumentList {Q(args)} -WorkingDirectory {Q(ExeDir)}";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false, CreateNoWindow = true,
        });
    }

    public static void CleanupAfterUpdate()
    {
        try
        {
            string tmp = Path.Combine(ExeDir, TmpName);
            if (File.Exists(tmp)) File.Delete(tmp);
            foreach (string f in Directory.EnumerateFiles(ExeDir, "IGP_Soft.*.borrar"))
            {
                try { File.Delete(f); }
                catch { }
            }
        }
        catch { }
    }

    public static void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
