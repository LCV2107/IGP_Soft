using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace IGP_Soft;

internal sealed record LinkDef(string Titulo, string Url);

internal static class LinkStore
{
    public static string FilePath { get; } =
        Path.Combine(AppPaths.DataDir, "IGP_Soft.enlaces.txt");

    public static List<LinkDef> Links { get; private set; } = new();

    public static Action<List<LinkDef>>? Remote { get; set; }

    public static void SetFromRemote(IEnumerable<LinkDef> items) => Links = items.ToList();

    private static readonly LinkDef[] Defaults =
    {
        new("Último sismo",                          "https://ultimosismo.igp.gob.pe/ultimo-sismo"),
        new("Reportes sísmicos",                     "https://ultimosismo.igp.gob.pe/productos/reportes-sismicos"),
        new("Última información volcánica (CENVUL)",  "https://cenvul.igp.gob.pe/ultima-informacion"),
        new("Lahares y huaicos",                     "https://grd.igp.gob.pe/lahares-huaicos/"),
    };

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Links = Defaults.ToList(); Save(); return; }

            var list = new List<LinkDef>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                int bar = line.IndexOf('|');
                string titulo = bar >= 0 ? line[..bar].Trim() : "";
                string url = Normalize(bar >= 0 ? line[(bar + 1)..].Trim() : line);
                if (url.Length == 0) continue;
                if (titulo.Length == 0) titulo = TitleFromUrl(url);
                list.Add(new LinkDef(Unique(titulo, list), url));
            }
            Links = list.Count > 0 ? list : Defaults.ToList();
        }
        catch { Links = Defaults.ToList(); }
    }

    public static void Save()
    {
        if (Remote is not null)
        {
            Remote(Links.ToList());
            return;
        }
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# IGP_Soft — lista de enlaces de monitoreo.");
            sb.AppendLine("# Un enlace por línea, formato:  Título | URL");
            sb.AppendLine("# Las líneas que empiezan con # se ignoran.");
            sb.AppendLine("# Se puede editar aquí o desde el botón «Enlaces…» del programa.");
            sb.AppendLine();
            foreach (LinkDef l in Links) sb.AppendLine($"{l.Titulo} | {l.Url}");
            File.WriteAllText(FilePath, sb.ToString());
        }
        catch { }
    }

    public static void Replace(IEnumerable<LinkDef> items)
    {
        var list = new List<LinkDef>();
        foreach (LinkDef it in items)
        {
            string url = Normalize(it.Url);
            if (url.Length == 0) continue;
            string t = string.IsNullOrWhiteSpace(it.Titulo) ? TitleFromUrl(url) : it.Titulo.Trim();
            list.Add(new LinkDef(Unique(t, list), url));
        }
        if (list.Count > 0) { Links = list; Save(); }
    }

    public static LinkDef Add(LinkDef it)
    {
        string url = Normalize(it.Url);
        string t = string.IsNullOrWhiteSpace(it.Titulo) ? TitleFromUrl(url) : it.Titulo.Trim();
        var stored = new LinkDef(Unique(t, Links), url);
        Links.Add(stored);
        Save();
        return stored;
    }

    private static string Normalize(string url)
    {
        url = url.Trim().Trim('"');
        if (url.Length == 0) return "";
        if (url.Contains("://")) return url;
        if (LooksLikePath(url)) return url;
        return "https://" + url;
    }

    private static bool LooksLikePath(string s) =>
        AssetStore.IsAssetPath(s) || (s.Length >= 2 && s[1] == ':') ||
        s.StartsWith(@"\\") || s.StartsWith("./") || s.StartsWith("../") ||
        s.StartsWith('/') || File.Exists(s);

    private static string TitleFromUrl(string url)
    {
        if (!url.Contains("://"))
        {
            try { string n = Path.GetFileName(url); if (n.Length > 0) return n; } catch { }
        }
        try { var u = new Uri(url); return (u.Host + u.AbsolutePath).TrimEnd('/'); }
        catch { return url; }
    }

    private static string Unique(string titulo, List<LinkDef> list)
    {
        string baseT = titulo;
        int n = 2;
        while (list.Any(x => string.Equals(x.Titulo, titulo, StringComparison.OrdinalIgnoreCase)))
            titulo = $"{baseT} ({n++})";
        return titulo;
    }
}

internal enum BrowserFamily { Chromium, Firefox }

internal sealed record Browser(string Name, BrowserFamily Family, string ExePath)
{
    public string ProcessName => Path.GetFileNameWithoutExtension(ExePath);
}

internal static class BrowserLocator
{
    private static readonly string[] PreferOrder = { "Edge", "Chrome", "Brave", "Firefox", "Opera" };

    public static List<Browser> InstalledAll()
    {
        var list = new List<Browser>();
        foreach (string name in PreferOrder)
        {
            Browser? b = Build(name);
            if (b is not null) list.Add(b);
        }
        return list;
    }

    public static string? DefaultName() => MapProgId(GetDefaultHttpProgId());

    public static Browser? Resolve(out string origen)
    {
        string? predeterminado = MapProgId(GetDefaultHttpProgId());
        if (predeterminado is not null)
        {
            Browser? b = Build(predeterminado);
            if (b is not null) { origen = "predeterminado de Windows"; return b; }
        }

        foreach (string name in PreferOrder)
        {
            Browser? b = Build(name);
            if (b is not null) { origen = "por orden de preferencia"; return b; }
        }

        origen = "";
        return null;
    }

    private static string? GetDefaultHttpProgId()
    {
        try
        {
            using RegistryKey? k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            return k?.GetValue("ProgId") as string;
        }
        catch { return null; }
    }

    private static string? MapProgId(string? progId)
    {
        if (string.IsNullOrWhiteSpace(progId)) return null;
        string p = progId.Trim();
        if (p.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase))  return "Edge";
        if (p.Contains("Brave", StringComparison.OrdinalIgnoreCase))     return "Brave";
        if (p.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))  return "Chrome";
        if (p.StartsWith("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (p.StartsWith("Opera", StringComparison.OrdinalIgnoreCase))   return "Opera";
        return null;
    }

    private static Browser? Build(string name)
    {
        (string exe, BrowserFamily fam, string[] candidates) = name switch
        {
            "Edge" => ("msedge.exe", BrowserFamily.Chromium, new[]
            {
                Env("ProgramFiles(x86)", @"Microsoft\Edge\Application\msedge.exe"),
                Env("ProgramFiles",      @"Microsoft\Edge\Application\msedge.exe"),
            }),
            "Chrome" => ("chrome.exe", BrowserFamily.Chromium, new[]
            {
                Env("ProgramFiles",      @"Google\Chrome\Application\chrome.exe"),
                Env("ProgramFiles(x86)", @"Google\Chrome\Application\chrome.exe"),
                Env("LocalAppData",      @"Google\Chrome\Application\chrome.exe"),
            }),
            "Brave" => ("brave.exe", BrowserFamily.Chromium, new[]
            {
                Env("ProgramFiles",      @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Env("ProgramFiles(x86)", @"BraveSoftware\Brave-Browser\Application\brave.exe"),
                Env("LocalAppData",      @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            }),
            "Firefox" => ("firefox.exe", BrowserFamily.Firefox, new[]
            {
                Env("ProgramFiles",      @"Mozilla Firefox\firefox.exe"),
                Env("ProgramFiles(x86)", @"Mozilla Firefox\firefox.exe"),
            }),
            "Opera" => ("opera.exe", BrowserFamily.Chromium, new[]
            {
                Env("LocalAppData",      @"Programs\Opera\opera.exe"),
                Env("ProgramFiles",      @"Opera\opera.exe"),
                Env("ProgramFiles(x86)", @"Opera\opera.exe"),
            }),
            _ => ("", BrowserFamily.Chromium, Array.Empty<string>()),
        };

        string? path = FromAppPaths(exe) ?? candidates.FirstOrDefault(c => c.Length > 0 && File.Exists(c));
        return path is null ? null : new Browser(name, fam, path);
    }

    private static string Env(string var, string tail)
    {
        string? root = Environment.GetEnvironmentVariable(var);
        return string.IsNullOrEmpty(root) ? "" : Path.Combine(root, tail);
    }

    private static string? FromAppPaths(string exe)
    {
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
        foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using RegistryKey? k = hive.OpenSubKey(sub + exe);
                if (k?.GetValue(null) is string p && File.Exists(p.Trim('"')))
                    return p.Trim('"');
            }
            catch { }
        }
        return null;
    }
}

internal static class Profiles
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "IGP_Soft_Profiles");

    public static void CleanupOld()
    {
        if (!Directory.Exists(Root)) return;
        foreach (string dir in Directory.EnumerateDirectories(Root))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { }
        }
    }

    public static string NewDir(string tag)
    {
        Directory.CreateDirectory(Root);
        string safe = string.Concat((tag ?? "s").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        if (safe.Length == 0) safe = "s";
        string d = Path.Combine(Root, $"{safe}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    public static readonly string KeepRoot = Path.Combine(AppPaths.DataDir, "IGP_Soft_Datos");

    public static bool KeepData
    {
        get => Config.Get("conservarDatos") == "1";
        set => Config.Set("conservarDatos", value ? "1" : "0");
    }

    public static bool IsKept(string dir) =>
        dir.StartsWith(KeepRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static bool HardwareAcceleration
    {
        get => Config.Get("aceleracion") != "0";
        set => Config.Set("aceleracion", value ? "1" : "0");
    }

    public static int ReloadHours
    {
        get => int.TryParse(Config.Get("recargaHoras"), out int h) && h > 0 ? h : 0;
        set => Config.Set("recargaHoras", value.ToString());
    }

    public static string? SharedDir(string browserName, bool keep)
    {
        try
        {
            string d = keep ? Path.Combine(KeepRoot, browserName, "compartido") : Path.Combine(Root, $"compartido_{browserName}");
            Directory.CreateDirectory(d);
            return d;
        }
        catch { return null; }
    }

    public static void DeleteKept()
    {
        try { if (Directory.Exists(KeepRoot)) Directory.Delete(KeepRoot, recursive: true); }
        catch { }
    }
}

internal sealed class LaunchedWindow
{
    public required int ProcessId { get; init; }
    public required IntPtr Hwnd { get; init; }
    public required string ProfileDir { get; init; }
    public required string ProcessName { get; init; }
    public required bool Borderless { get; init; }
    public IntPtr ParentHwnd { get; init; }
    public Rectangle DesiredArea;
    public volatile bool Closed;
    public bool Attached { get; init; }
    public long OriginalStyle { get; init; }
    public Rectangle OriginalBounds { get; init; }
    public bool WasMaximized { get; init; }
    public bool Released;
    public volatile bool Hidden;
    public bool Native { get; init; }
    public object? Tag { get; init; }
    public Rectangle Insets;
    public Rectangle AppliedRegion;
    public int StyleFixes;
    public bool ReportedHung;
    public readonly object AreaGate = new();
    public Rectangle PendingArea;
    public bool AreaDirty;
}

internal sealed record OpenWindow(IntPtr Hwnd, int Pid, string ProcessName, string Title);

internal static class ProcessInfo
{
    public static string? CommandLine(int pid)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementBaseObject o in s.Get()) return o["CommandLine"] as string;
        }
        catch { }
        return null;
    }

    public static List<int> ByCommandLineContains(string name, string needle)
    {
        var pids = new List<int>();
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{name}.exe'");
            foreach (ManagementBaseObject o in s.Get())
            {
                string? cl = o["CommandLine"] as string;
                if (cl is not null && cl.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    pids.Add(Convert.ToInt32(o["ProcessId"]));
            }
        }
        catch { }
        return pids;
    }
}

internal static class MediaWrapper
{
    private static readonly string[] ImgExt =
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".avif", ".apng", ".jfif" };
    private static readonly string[] VidExt =
        { ".mp4", ".webm", ".ogv", ".ogg", ".mov", ".m4v", ".mkv" };

    public static string ToBrowserUrl(string s)
    {
        s = s.Trim().Trim('"');
        if (s.Length == 0) return s;
        if (s.Contains("://")) return s;
        s = AssetStore.Resolve(s);
        try { return new Uri(Path.GetFullPath(s)).AbsoluteUri; }
        catch { return s; }
    }

    public static string? Prepare(string target, string dir)
    {
        string ext = ExtensionOf(target).ToLowerInvariant();
        bool img = ImgExt.Contains(ext);
        bool vid = VidExt.Contains(ext);
        if (!img && !vid) return null;

        string src = System.Net.WebUtility.HtmlEncode(ToBrowserUrl(target));
        string element = img
            ? $"<img src=\"{src}\" alt=\"\">"
            : $"<video src=\"{src}\" autoplay muted loop playsinline></video>";

        string html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>IGP_Soft</title><style>" +
            "html,body{margin:0;width:100%;height:100%;background:#fff;overflow:hidden}" +
            "img,video{position:fixed;inset:0;width:100%;height:100%;object-fit:contain}" +
            "</style></head><body>" + element + "</body></html>";

        string path = Path.Combine(dir, $"view_{Guid.NewGuid():N}.html");
        File.WriteAllText(path, html, new UTF8Encoding(false));
        return new Uri(path).AbsoluteUri;
    }

    private static string ExtensionOf(string target)
    {
        try
        {
            if (target.Contains("://"))
                return Path.GetExtension(new Uri(target).AbsolutePath);
            return Path.GetExtension(target);
        }
        catch { return ""; }
    }
}

internal static class WindowManager
{
    private static readonly object LaunchGate = new();

    private static string ToBrowserUrl(string s) => MediaWrapper.ToBrowserUrl(s);

    public static LaunchedWindow? LaunchAndPlace(Browser browser, string url, Rectangle area, string tag,
        IntPtr parentHwnd = default, bool keepData = false)
    {
        lock (LaunchGate)
        {
            string profileDir = Profiles.SharedDir(browser.Name, keepData) ?? Profiles.NewDir(tag);
            bool running = HasLiveWindows(profileDir);
            if (!running) KillProfileProcesses(browser.ProcessName, profileDir);
            url = MediaWrapper.Prepare(url, profileDir) ?? ToBrowserUrl(url);
            var before = SnapshotWindows(browser.ProcessName);

            Rectangle onScreen = area;
            double scale = 1.0;
            if (parentHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(parentHwnd, out NativeMethods.RECT pr))
            {
                onScreen.Offset(pr.Left, pr.Top);
                uint dpi = NativeMethods.GetDpiForWindow(parentHwnd);
                if (dpi > 0) scale = dpi / 96.0;
            }

            var psi = new ProcessStartInfo
            {
                FileName = browser.ExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (browser.Family == BrowserFamily.Chromium)
            {
                psi.ArgumentList.Add($"--user-data-dir={profileDir}");
                psi.ArgumentList.Add("--no-first-run");
                psi.ArgumentList.Add("--no-default-browser-check");
                psi.ArgumentList.Add("--disable-session-crashed-bubble");
                psi.ArgumentList.Add("--hide-crash-restore-bubble");
                psi.ArgumentList.Add("--disable-infobars");
                psi.ArgumentList.Add("--disable-features=Translate,IsolateOrigins,site-per-process,MediaRouter,OptimizationHints,AutofillServerCommunication");
                psi.ArgumentList.Add("--renderer-process-limit=4");
                psi.ArgumentList.Add("--disable-extensions");
                psi.ArgumentList.Add("--disable-component-update");
                psi.ArgumentList.Add("--disable-background-networking");
                psi.ArgumentList.Add("--disable-sync");
                psi.ArgumentList.Add("--disable-default-apps");
                psi.ArgumentList.Add("--disable-breakpad");
                psi.ArgumentList.Add("--disable-domain-reliability");
                psi.ArgumentList.Add("--disable-client-side-phishing-detection");
                psi.ArgumentList.Add("--disable-component-extensions-with-background-pages");
                psi.ArgumentList.Add("--no-pings");
                psi.ArgumentList.Add("--metrics-recording-only");
                if (!Profiles.HardwareAcceleration) psi.ArgumentList.Add("--disable-gpu");
                psi.ArgumentList.Add("--disable-search-engine-choice-screen");
                psi.ArgumentList.Add($"--window-position={onScreen.X},{onScreen.Y}");
                psi.ArgumentList.Add($"--window-size={area.Width},{area.Height}");
                if (running) psi.ArgumentList.Add("--new-window");
                psi.ArgumentList.Add("--kiosk");
                if (browser.Name.Equals("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    psi.ArgumentList.Add("--edge-kiosk-type=fullscreen");
                    psi.ArgumentList.Add("--kiosk-idle-timeout-minutes=0");
                }
                psi.ArgumentList.Add(url);
            }
            else
            {
                if (!running) WriteFirefoxProfile(profileDir, onScreen, scale);
                psi.ArgumentList.Add("-profile");
                psi.ArgumentList.Add(profileDir);
                if (running) psi.ArgumentList.Add("-new-window");
                psi.ArgumentList.Add(url);
            }

            try
            {
                Process? started = Process.Start(psi);
                if (started is not null)
                {
                    started.OutputDataReceived += static (_, _) => { };
                    started.ErrorDataReceived += static (_, _) => { };
                    started.BeginOutputReadLine();
                    started.BeginErrorReadLine();
                }
            }
            catch
            {
                if (!HasLiveWindows(profileDir)) CleanupProfile(browser.ProcessName, profileDir);
                return null;
            }

            IntPtr hwnd = IntPtr.Zero;
            int pid = 0;
            var ownPid = new Dictionary<int, bool>();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 15000 && hwnd == IntPtr.Zero)
            {
                Thread.Sleep(120);
                foreach ((IntPtr h, int p) in EnumBrowserWindows(browser.ProcessName))
                {
                    if (before.Contains(h)) continue;
                    if (!ownPid.TryGetValue(p, out bool ours))
                    {
                        string? cl = ProcessInfo.CommandLine(p);
                        ours = cl is not null && cl.Contains(profileDir, StringComparison.OrdinalIgnoreCase);
                        if (cl is not null) ownPid[p] = ours;
                    }
                    if (ours) { hwnd = h; pid = p; break; }
                }
            }

            if (hwnd == IntPtr.Zero)
            {
                if (!HasLiveWindows(profileDir)) CleanupProfile(browser.ProcessName, profileDir);
                return null;
            }

            const bool borderless = true;
            var win = new LaunchedWindow
            {
                ProcessId = pid, Hwnd = hwnd, ProfileDir = profileDir,
                ProcessName = browser.ProcessName, Borderless = borderless,
                ParentHwnd = parentHwnd, DesiredArea = area,
            };
            lock (SharedWindows) SharedWindows.Add(win);

            PlaceWindow(win);
            Task.Run(() =>
            {
                for (int i = 0; i < 24 && !win.Closed; i++)
                {
                    Thread.Sleep(500);
                    if (NativeMethods.IsWindow(hwnd)) PlaceWindow(win);
                    else break;
                }
            });

            return win;
        }
    }

    public static void Reposition(LaunchedWindow win, Rectangle area)
    {
        if (win.Attached)
        {
            lock (win.AreaGate)
            {
                win.PendingArea = area;
                win.AreaDirty = true;
            }
            return;
        }
        try
        {
            lock (win)
            {
                win.DesiredArea = area;
                if (NativeMethods.IsWindow(win.Hwnd))
                    PlaceWindow(win);
            }
        }
        catch { }
    }

    private static readonly List<LaunchedWindow> SharedWindows = new();

    private static bool HasLiveWindows(string profileDir)
    {
        lock (SharedWindows)
        {
            SharedWindows.RemoveAll(w => w.Closed || !NativeMethods.IsWindow(w.Hwnd));
            return SharedWindows.Any(w => string.Equals(w.ProfileDir, profileDir, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static IReadOnlyList<int> BrowserRootPids()
    {
        lock (SharedWindows)
            return SharedWindows.Where(w => !w.Closed).Select(w => w.ProcessId).Distinct().ToList();
    }

    public static void Close(LaunchedWindow win)
    {
        if (win.Attached) { Release(win); return; }
        if (win.Native) return;
        win.Closed = true;
        lock (SharedWindows) SharedWindows.Remove(win);

        try
        {
            if (win.Hwnd != IntPtr.Zero && NativeMethods.IsWindow(win.Hwnd))
                NativeMethods.PostMessage(win.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }

        Thread.Sleep(900);

        if (HasLiveWindows(win.ProfileDir))
        {
            for (int i = 0; i < 6 && NativeMethods.IsWindow(win.Hwnd); i++)
            {
                NativeMethods.PostMessage(win.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(500);
            }
            return;
        }

        lock (LaunchGate)
        {
            if (!HasLiveWindows(win.ProfileDir))
                CleanupProfile(win.ProcessName, win.ProfileDir);
        }
    }

    private static void KillProfileProcesses(string processName, string profileDir)
    {
        foreach (int pid in ProcessInfo.ByCommandLineContains(processName, profileDir))
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
            catch { }
        }
    }

    private static void CleanupProfile(string processName, string profileDir)
    {
        KillProfileProcesses(processName, profileDir);
        if (Profiles.IsKept(profileDir)) return;

        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(profileDir)) Directory.Delete(profileDir, recursive: true);
                break;
            }
            catch { Thread.Sleep(400); }
        }
    }

    public const string WindowScheme = "ventana:";

    public static bool IsWindowSpec(string url) => url.StartsWith(WindowScheme, StringComparison.OrdinalIgnoreCase);

    public static string SpecOf(OpenWindow w) => $"{WindowScheme}{w.ProcessName}|{w.Title}";

    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "ApplicationFrameWindow", "Windows.UI.Core.CoreWindow",
    };

    public static List<OpenWindow> ListOpenWindows()
    {
        var result = new List<OpenWindow>();
        int self = Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            int len = NativeMethods.GetWindowTextLength(hWnd);
            if (len == 0) return true;
            if (NativeMethods.IsHungAppWindow(hWnd)) return true;
            if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;
            long ex = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0) return true;

            var cls = new StringBuilder(64);
            NativeMethods.GetClassName(hWnd, cls, cls.Capacity);
            if (ShellClasses.Contains(cls.ToString())) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == self) return true;

            var title = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hWnd, title, title.Capacity);
            try
            {
                using var pr = Process.GetProcessById((int)pid);
                result.Add(new OpenWindow(hWnd, (int)pid, pr.ProcessName, title.ToString()));
            }
            catch { }
            return true;
        }, IntPtr.Zero);

        return result.OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static OpenWindow? FindOpenWindow(string spec)
    {
        string body = spec[WindowScheme.Length..];
        int bar = body.IndexOf('|');
        string proc = bar >= 0 ? body[..bar] : body;
        string title = bar >= 0 ? body[(bar + 1)..] : "";

        var same = ListOpenWindows().Where(w => w.ProcessName.Equals(proc, StringComparison.OrdinalIgnoreCase)).ToList();
        return same.FirstOrDefault(w => w.Title == title) ?? same.FirstOrDefault();
    }

    public static (LaunchedWindow? Win, string? Error) Attach(string spec, Rectangle area, IntPtr parentHwnd)
    {
        OpenWindow? w = FindOpenWindow(spec);
        if (w is null) return (null, "La ventana ya no está abierta.");

        IntPtr h = w.Hwnd;
        Diagnostico.Log($"Adjuntar «{w.Title}» — {w.ProcessName} (pid {w.Pid}), {Diagnostico.Awareness(h)}");
        var sw = Stopwatch.StartNew();
        if (!Responds(h, 3000))
        {
            Diagnostico.Log($"  no respondió en {sw.ElapsedMilliseconds} ms: se cancela");
            return (null, "La aplicación no responde (puede estar cargando). Espere a que termine y vuelva a intentarlo.");
        }
        Diagnostico.Log($"  responde en {sw.ElapsedMilliseconds} ms");

        bool maximized = NativeMethods.IsZoomed(h);
        Rectangle original = NormalBounds(h);
        if (maximized || NativeMethods.IsIconic(h)) NativeMethods.ShowWindowAsync(h, NativeMethods.SW_RESTORE);

        var win = new LaunchedWindow
        {
            ProcessId = w.Pid, Hwnd = h, ProfileDir = "", ProcessName = w.ProcessName, Borderless = true,
            ParentHwnd = parentHwnd, DesiredArea = area, Attached = true,
            OriginalStyle = NativeMethods.GetWindowLongPtr(h, NativeMethods.GWL_STYLE).ToInt64(),
            OriginalBounds = original,
            WasMaximized = maximized,
        };

        sw.Restart();
        PlaceWindow(win, probeMs: 3000);
        Diagnostico.Log($"  integrada en {sw.ElapsedMilliseconds} ms");
        if (NativeMethods.GetParent(h) != parentHwnd)
        {
            bool hung = win.ReportedHung;
            Diagnostico.Log(hung ? "  dejó de responder mientras se integraba" : "  Windows no permitió integrarla");
            Release(win);
            return (null, hung
                ? "La aplicación dejó de responder mientras se adjuntaba. Espere a que termine de cargar y vuelva a intentarlo."
                : "Windows no permitió integrar la ventana. Si esa aplicación se ejecuta como administrador, abra IGP_Soft también como administrador.");
        }

        lock (AttachedWindows) AttachedWindows.Add(win);
        StartKeeper(win);
        return (win, null);
    }

    public static bool Responds(IntPtr hwnd, uint timeoutMs) =>
        NativeMethods.SendMessageTimeout(hwnd, 0, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, timeoutMs, out _) != IntPtr.Zero;

    private static Rectangle NormalBounds(IntPtr h)
    {
        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if ((NativeMethods.IsZoomed(h) || NativeMethods.IsIconic(h)) && NativeMethods.GetWindowPlacement(h, ref wp))
            return Rectangle.FromLTRB(wp.rcNormalPosition.Left, wp.rcNormalPosition.Top, wp.rcNormalPosition.Right, wp.rcNormalPosition.Bottom);
        NativeMethods.GetWindowRect(h, out NativeMethods.RECT r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    private static void StartKeeper(LaunchedWindow win)
    {
        var t = new Thread(() =>
        {
            while (!win.Closed && NativeMethods.IsWindow(win.Hwnd))
            {
                Thread.Sleep(400);
                if (win.Closed) break;
                Rectangle? next = null;
                lock (win.AreaGate)
                {
                    if (win.AreaDirty)
                    {
                        next = win.PendingArea;
                        win.AreaDirty = false;
                    }
                }
                try
                {
                    lock (win)
                    {
                        if (next is { } a) win.DesiredArea = a;
                    }
                    PlaceWindow(win, probeMs: 2000);
                }
                catch { }
            }
        })
        { IsBackground = true, Name = "IGP_Soft ventana adjunta" };
        t.Start();
    }

    private static readonly HashSet<LaunchedWindow> AttachedWindows = new();

    public static void ReleaseAll()
    {
        List<LaunchedWindow> all;
        lock (AttachedWindows) all = AttachedWindows.ToList();
        foreach (LaunchedWindow w in all) Release(w);
    }

    public static Task ReleaseAsync(LaunchedWindow win)
    {
        win.Closed = true;
        return Task.Run(() => Release(win));
    }

    public static void Release(LaunchedWindow win)
    {
        lock (AttachedWindows) AttachedWindows.Remove(win);
        win.Closed = true;
        lock (win)
        {
            if (win.Released) return;
            win.Released = true;

            IntPtr h = win.Hwnd;
            if (!NativeMethods.IsWindow(h)) return;
            var sw = Stopwatch.StartNew();
            NativeMethods.SetParent(h, IntPtr.Zero);
            NativeMethods.SetWindowRgn(h, IntPtr.Zero, false);
            NativeMethods.SetWindowLongPtr(h, NativeMethods.GWL_STYLE, new IntPtr(win.OriginalStyle));
            Rectangle b = win.OriginalBounds;
            NativeMethods.SetWindowPos(h, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW |
                NativeMethods.SWP_ASYNCWINDOWPOS);
            if (win.WasMaximized) NativeMethods.ShowWindowAsync(h, NativeMethods.SW_MAXIMIZE);
            if (sw.ElapsedMilliseconds >= 200)
                Diagnostico.Log($"«{win.ProcessName}» tardó {sw.ElapsedMilliseconds} ms en volver al escritorio");
        }
    }

    private const int MaxStyleFixes = 6;

    private static void PlaceWindow(LaunchedWindow win, uint probeMs = 0)
    {
        lock (win)
        {
            if (win.Attached && win.Closed) return;
            IntPtr hwnd = win.Hwnd;
            bool foreign = win.Attached;
            bool? responds = null;

            bool Ready()
            {
                if (probeMs == 0) return true;
                responds ??= Responds(hwnd, probeMs);
                if (responds == false && !win.ReportedHung)
                {
                    win.ReportedHung = true;
                    Diagnostico.Log($"«{win.ProcessName}» no responde: se espera sin tocar su ventana");
                }
                else if (responds == true && win.ReportedHung)
                {
                    win.ReportedHung = false;
                    Diagnostico.Log($"«{win.ProcessName}» vuelve a responder");
                }
                return responds == true;
            }

            if (win.Hidden)
            {
                if (NativeMethods.IsWindowVisible(hwnd) && Ready()) Show(hwnd, 0, foreign);
                return;
            }
            Rectangle area = win.DesiredArea;
            if (NativeMethods.IsIconic(hwnd))
            {
                if (!Ready()) return;
                Show(hwnd, NativeMethods.SW_RESTORE, foreign);
            }

            bool frameChanged = false;
            if (win.Borderless && !(foreign && win.StyleFixes >= MaxStyleFixes))
            {
                long cur = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
                long want = cur & ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME | NativeMethods.WS_POPUP |
                                    NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_MAXIMIZE);
                if (win.ParentHwnd != IntPtr.Zero) want |= NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS;
                if (want != cur)
                {
                    if (!Ready()) return;
                    if (foreign && ++win.StyleFixes == MaxStyleFixes)
                        Diagnostico.Log($"«{win.ProcessName}» vuelve a poner sus bordes una y otra vez: se dejan de forzar");
                    Timed(win, "quitar bordes", () => NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(want)));
                    frameChanged = true;
                }

                long curEx = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                long wantEx = curEx & ~(NativeMethods.WS_EX_DLGMODALFRAME | NativeMethods.WS_EX_WINDOWEDGE |
                                        NativeMethods.WS_EX_CLIENTEDGE | NativeMethods.WS_EX_STATICEDGE);
                if (wantEx != curEx)
                {
                    if (!Ready()) return;
                    Timed(win, "quitar marco", () => NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(wantEx)));
                    frameChanged = true;
                }
            }

            if (win.ParentHwnd != IntPtr.Zero && NativeMethods.GetParent(hwnd) != win.ParentHwnd)
            {
                if (!Ready()) return;
                Timed(win, "integrar en la pantalla", () => NativeMethods.SetParent(hwnd, win.ParentHwnd));
                frameChanged = true;
            }

            Rectangle target = Expand(area, win.Insets);
            if (!frameChanged && win.ParentHwnd != IntPtr.Zero &&
                NativeMethods.IsWindowVisible(hwnd) && IsAt(hwnd, win.ParentHwnd, target))
                return;
            if (!Ready()) return;

            uint async = foreign ? NativeMethods.SWP_ASYNCWINDOWPOS : 0;
            uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER | async;
            if (frameChanged) flags |= NativeMethods.SWP_FRAMECHANGED;
            IntPtr order = win.ParentHwnd == IntPtr.Zero ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
            Timed(win, "colocar", () => NativeMethods.SetWindowPos(hwnd, order, target.X, target.Y, target.Width, target.Height, flags));

            if (win.ParentHwnd == IntPtr.Zero) return;

            Rectangle insets = MeasureInsets(hwnd);
            if (insets != win.Insets)
            {
                win.Insets = insets;
                target = Expand(area, insets);
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER | async);
            }

            var region = new Rectangle(insets.X, insets.Y, area.Width, area.Height);
            bool needsRegion = insets != Rectangle.Empty;
            if (needsRegion ? region != win.AppliedRegion : win.AppliedRegion != Rectangle.Empty)
            {
                IntPtr rgn = needsRegion
                    ? NativeMethods.CreateRectRgn(region.Left, region.Top, region.Right, region.Bottom)
                    : IntPtr.Zero;
                int applied = 0;
                Timed(win, "recortar el borde", () => applied = NativeMethods.SetWindowRgn(hwnd, rgn, true));
                if (applied == 0 && rgn != IntPtr.Zero)
                    NativeMethods.DeleteObject(rgn);
                win.AppliedRegion = needsRegion ? region : Rectangle.Empty;
            }
        }
    }

    private static void Show(IntPtr hwnd, int cmd, bool async)
    {
        if (async) NativeMethods.ShowWindowAsync(hwnd, cmd);
        else NativeMethods.ShowWindow(hwnd, cmd);
    }

    private static void Timed(LaunchedWindow win, string step, Action action)
    {
        if (!win.Attached)
        {
            action();
            return;
        }
        var sw = Stopwatch.StartNew();
        action();
        if (sw.ElapsedMilliseconds >= 200)
            Diagnostico.Log($"«{win.ProcessName}» tardó {sw.ElapsedMilliseconds} ms en: {step}");
    }

    private static Rectangle Expand(Rectangle area, Rectangle insets) =>
        new(area.X - insets.X, area.Y - insets.Y, area.Width + insets.X + insets.Width, area.Height + insets.Y + insets.Height);

    private static Rectangle MeasureInsets(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT w) ||
            !NativeMethods.GetClientRect(hwnd, out NativeMethods.RECT c))
            return Rectangle.Empty;
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return Rectangle.Empty;

        int left = origin.X - w.Left;
        int top = origin.Y - w.Top;
        int right = (w.Right - w.Left) - left - (c.Right - c.Left);
        int bottom = (w.Bottom - w.Top) - top - (c.Bottom - c.Top);
        if (left < 0 || top < 0 || right < 0 || bottom < 0 || left + top + right + bottom > 200) return Rectangle.Empty;
        return new Rectangle(left, top, right, bottom);
    }

    private static bool IsAt(IntPtr hwnd, IntPtr parent, Rectangle area)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r) ||
            !NativeMethods.GetWindowRect(parent, out NativeMethods.RECT p))
            return false;
        return r.Left - p.Left == area.X && r.Top - p.Top == area.Y &&
               r.Right - r.Left == area.Width && r.Bottom - r.Top == area.Height;
    }

    private static void WriteFirefoxProfile(string dir, Rectangle onScreen, double scale)
    {
        try
        {
            int Css(int v) => (int)Math.Round(v / scale);
            File.WriteAllText(Path.Combine(dir, "xulstore.json"),
                "{\"chrome://browser/content/browser.xhtml\":{\"main-window\":{" +
                $"\"screenX\":\"{Css(onScreen.X)}\",\"screenY\":\"{Css(onScreen.Y)}\"," +
                $"\"width\":\"{Css(onScreen.Width)}\",\"height\":\"{Css(onScreen.Height)}\",\"sizemode\":\"normal\"}}}}");
            File.WriteAllText(Path.Combine(dir, "user.js"),
                "user_pref(\"datareporting.policy.dataSubmissionPolicyBypassNotification\", true);\n" +
                "user_pref(\"datareporting.policy.dataSubmissionEnabled\", false);\n" +
                "user_pref(\"datareporting.healthreport.uploadEnabled\", false);\n" +
                "user_pref(\"toolkit.telemetry.reportingpolicy.firstRun\", false);\n" +
                "user_pref(\"toolkit.telemetry.enabled\", false);\n" +
                "user_pref(\"browser.shell.checkDefaultBrowser\", false);\n" +
                "user_pref(\"browser.startup.homepage_override.mstone\", \"ignore\");\n" +
                "user_pref(\"startup.homepage_welcome_url\", \"\");\n" +
                "user_pref(\"startup.homepage_welcome_url.additional\", \"\");\n" +
                "user_pref(\"startup.homepage_override_url\", \"\");\n" +
                "user_pref(\"browser.startup.firstrunSkipsHomepage\", true);\n" +
                "user_pref(\"browser.aboutwelcome.enabled\", false);\n" +
                "user_pref(\"browser.messaging-system.whatsNewPanel.enabled\", false);\n" +
                "user_pref(\"trailhead.firstrun.didSeeAboutWelcome\", true);\n" +
                "user_pref(\"browser.laterrun.enabled\", false);\n" +
                "user_pref(\"browser.fullscreen.autohide\", false);\n" +
                "user_pref(\"browser.tabs.warnOnClose\", false);\n" +
                "user_pref(\"browser.tabs.inTitlebar\", 0);\n" +
                "user_pref(\"fission.autostart\", false);\n" +
                "user_pref(\"dom.ipc.processCount\", 4);\n" +
                "user_pref(\"dom.ipc.processCount.webIsolated\", 1);\n" +
                "user_pref(\"dom.ipc.processPrelaunch.enabled\", false);\n" +
                "user_pref(\"browser.tabs.unloadOnLowMemory\", true);\n" +
                "user_pref(\"browser.sessionstore.interval\", 3600000);\n" +
                "user_pref(\"browser.sessionstore.max_tabs_undo\", 0);\n" +
                "user_pref(\"browser.sessionhistory.max_entries\", 10);\n" +
                "user_pref(\"browser.cache.memory.capacity\", 32768);\n" +
                "user_pref(\"media.memory_cache_max_size\", 16384);\n" +
                "user_pref(\"network.prefetch-next\", false);\n" +
                "user_pref(\"network.dns.disablePrefetch\", true);\n" +
                "user_pref(\"network.predictor.enabled\", false);\n" +
                "user_pref(\"browser.safebrowsing.malware.enabled\", false);\n" +
                "user_pref(\"browser.safebrowsing.phishing.enabled\", false);\n" +
                "user_pref(\"browser.safebrowsing.downloads.enabled\", false);\n" +
                "user_pref(\"extensions.pocket.enabled\", false);\n" +
                "user_pref(\"extensions.screenshots.disabled\", true);\n" +
                "user_pref(\"browser.discovery.enabled\", false);\n" +
                "user_pref(\"app.normandy.enabled\", false);\n" +
                "user_pref(\"app.shield.optoutstudies.enabled\", false);\n" +
                "user_pref(\"browser.ping-centre.telemetry\", false);\n" +
                "user_pref(\"browser.newtabpage.enabled\", false);\n" +
                "user_pref(\"layout.frame_rate\", 30);\n" +
                (Profiles.HardwareAcceleration ? "" :
                    "user_pref(\"layers.acceleration.disabled\", true);\n" +
                    "user_pref(\"gfx.webrender.software\", true);\n" +
                    "user_pref(\"media.hardware-video-decoding.enabled\", false);\n") +
                "user_pref(\"full-screen-api.ignore-widgets\", true);\n" +
                "user_pref(\"browser.link.open_newwindow\", 1);\n" +
                "user_pref(\"browser.link.open_newwindow.restriction\", 0);\n" +
                "user_pref(\"dom.disable_window_move_resize\", true);\n" +
                "user_pref(\"browser.startup.preXulSkeletonUI\", false);\n" +
                "user_pref(\"browser.sessionstore.resume_from_crash\", false);\n" +
                "user_pref(\"browser.sessionstore.resume_session_once\", false);\n" +
                "user_pref(\"browser.startup.page\", 0);\n" +
                "user_pref(\"toolkit.legacyUserProfileCustomizations.stylesheets\", true);\n" +
                "user_pref(\"security.fileuri.strict_origin_policy\", false);\n" +
                "user_pref(\"media.autoplay.default\", 0);\n" +
                "user_pref(\"media.autoplay.blocking_policy\", 0);\n");

            string chromeDir = Path.Combine(dir, "chrome");
            Directory.CreateDirectory(chromeDir);
            File.WriteAllText(Path.Combine(chromeDir, "userChrome.css"),
                "#navigator-toolbox, #titlebar, #TabsToolbar, #nav-bar, #PersonalToolbar, #sidebar-box, #sidebar-main " +
                "{ visibility: collapse !important; border: 0 !important; }\n" +
                ":root, #navigator-toolbox, #browser, #tabbrowser-tabbox, #tabbrowser-tabpanels, .browserContainer, .browserStack " +
                "{ border: 0 !important; margin: 0 !important; padding: 0 !important; outline: none !important; box-shadow: none !important; }\n");
        }
        catch { }
    }

    private static HashSet<IntPtr> SnapshotWindows(string procName)
    {
        var set = new HashSet<IntPtr>();
        foreach ((IntPtr h, int _) in EnumBrowserWindows(procName)) set.Add(h);
        return set;
    }

    private static IEnumerable<(IntPtr Hwnd, int Pid)> EnumBrowserWindows(string procName)
    {
        var result = new List<(IntPtr, int)>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (NativeMethods.GetWindowTextLength(hWnd) == 0) return true;

            var cls = new System.Text.StringBuilder(64);
            NativeMethods.GetClassName(hWnd, cls, cls.Capacity);
            string c = cls.ToString();
            if (c != "MozillaWindowClass" && c != "Chrome_WidgetWin_1") return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            try
            {
                using var pr = Process.GetProcessById((int)pid);
                if (string.Equals(pr.ProcessName, procName, StringComparison.OrdinalIgnoreCase))
                    result.Add((hWnd, (int)pid));
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        return result;
    }
}

internal static class ResourceMonitor
{
    public static long BrowserMemory()
    {
        var roots = WindowManager.BrowserRootPids();
        if (roots.Count == 0) return 0;
        var children = new Dictionary<int, List<int>>();
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == new IntPtr(-1)) return 0;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (Process32FirstW(snap, ref e))
            {
                do
                {
                    int ppid = (int)e.th32ParentProcessID;
                    if (!children.TryGetValue(ppid, out List<int>? list)) children[ppid] = list = new List<int>();
                    list.Add((int)e.th32ProcessID);
                } while (Process32NextW(snap, ref e));
            }
        }
        finally { NativeMethods.CloseHandle(snap); }

        var all = new HashSet<int>(roots);
        var queue = new Queue<int>(roots);
        while (queue.Count > 0)
            if (children.TryGetValue(queue.Dequeue(), out List<int>? kids))
                foreach (int k in kids)
                    if (all.Add(k)) queue.Enqueue(k);

        long total = 0;
        foreach (int pid in all)
        {
            try { using var pr = Process.GetProcessById(pid); total += pr.PrivateMemorySize64; }
            catch { }
        }
        return total;
    }

    public static long PhysicalMemory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (long)m.ullTotalPhys : 0;
    }

    public static string Format(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB" : $"{bytes / (1024 * 1024)} MB";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

internal static class WindowWatcher
{
    private static NativeMethods.WinEventProc? _proc;
    private static IntPtr _hook;

    public static void Start(Action<IntPtr> moved)
    {
        if (_hook != IntPtr.Zero) return;
        _proc = (_, _, hwnd, idObject, idChild, _, _) =>
        {
            if (idObject == 0 && idChild == 0 && hwnd != IntPtr.Zero) moved(hwnd);
        };
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
            NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}

internal sealed record MonitorInfo(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary)
{
    public string ShortName => DeviceName.Replace(@"\\.\", "");
    public IReadOnlyList<MonitorInfo>? Members { get; init; }
    public bool IsCombined => Members is { Count: > 1 };
    public string HardwareId { get; init; } = "";
    public string Id => HardwareId.Length > 0 ? HardwareId : DeviceName;
}

internal static class MonitorEnumerator
{
    public static List<MonitorInfo> GetOrdered()
    {
        var list = new List<MonitorInfo>();

        NativeMethods.MonitorEnumProc cb = (IntPtr hMon, IntPtr hdc, ref NativeMethods.RECT r, IntPtr data) =>
        {
            var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
            {
                list.Add(new MonitorInfo(
                    mi.szDevice,
                    mi.rcMonitor.Left,
                    mi.rcMonitor.Top,
                    mi.rcMonitor.Right - mi.rcMonitor.Left,
                    mi.rcMonitor.Bottom - mi.rcMonitor.Top,
                    (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0)
                {
                    HardwareId = HardwareIdOf(mi.szDevice),
                });
            }
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);

        return list
            .OrderBy(m => ExtractIndex(m.DeviceName))
            .ThenBy(m => m.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string HardwareIdOf(string deviceName)
    {
        try
        {
            var dd = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
            if (!NativeMethods.EnumDisplayDevices(deviceName, 0, ref dd, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME)) return "";
            return HardwareIdFromInterface(dd.DeviceID);
        }
        catch { return ""; }
    }

    public static string HardwareIdFromInterface(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string[] parts = path.Split('#');
        if (parts.Length < 3) return "";
        string model = new string(parts[1].Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (model.Length == 0) return "";
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(parts[2].ToUpperInvariant()));
        return $"MON-{model}-{Convert.ToHexString(hash, 0, 4)}";
    }

    private static int ExtractIndex(string deviceName)
    {
        var digits = new string(deviceName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : int.MaxValue;
    }
}

internal static class NativeMethods
{
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const uint SMTO_BLOCK = 0x0001;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public const int SW_RESTORE = 9;
    public const int SW_MAXIMIZE = 3;
    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_DLGMODALFRAME = 0x00000001;
    public const long WS_EX_WINDOWEDGE = 0x00000100;
    public const long WS_EX_CLIENTEDGE = 0x00000200;
    public const long WS_EX_STATICEDGE = 0x00020000;
    public const long WS_MINIMIZEBOX = 0x00020000;
    public const long WS_MAXIMIZEBOX = 0x00010000;
    public const long WS_MAXIMIZE = 0x01000000;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int DWMWA_CLOAKED = 14;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint MONITORINFOF_PRIMARY = 0x1;
    public const uint WM_CLOSE = 0x0010;
    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_CHILD = 0x40000000;
    public const long WS_CLIPSIBLINGS = 0x04000000;
    public const long WS_POPUP = unchecked((long)0x80000000);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
