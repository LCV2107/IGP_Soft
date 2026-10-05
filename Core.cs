using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace IGP_Soft;

// ---------------------------------------------------------------------------
//  Enlaces del IGP (orden fijo pedido)
// ---------------------------------------------------------------------------

internal sealed record LinkDef(string Titulo, string Url);

/// <summary>
/// Lista de enlaces editable, guardada en un archivo de texto junto al ejecutable
/// (portable). Formato por línea:  Título | URL
/// </summary>
internal static class LinkStore
{
    public static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "IGP_Soft.enlaces.txt");

    public static List<LinkDef> Links { get; private set; } = new();

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
        catch { /* carpeta de solo lectura: los cambios no se conservan */ }
    }

    /// <summary>Reemplaza la lista (normaliza URLs y títulos) y la guarda.</summary>
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

    /// <summary>Agrega un enlace al final (título único), guarda y devuelve el guardado.</summary>
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
        if (LooksLikePath(url)) return url;                 // ruta local: se deja tal cual
        return "https://" + url;
    }

    private static bool LooksLikePath(string s) =>
        (s.Length >= 2 && s[1] == ':') ||
        s.StartsWith(@"\\") || s.StartsWith("./") || s.StartsWith("../") ||
        s.StartsWith('/') || File.Exists(s);

    private static string TitleFromUrl(string url)
    {
        if (!url.Contains("://"))
        {
            try { string n = Path.GetFileName(url); if (n.Length > 0) return n; } catch { /* ignore */ }
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

// ---------------------------------------------------------------------------
//  Navegador
// ---------------------------------------------------------------------------

internal enum BrowserFamily { Chromium, Firefox }

internal sealed record Browser(string Name, BrowserFamily Family, string ExePath)
{
    public string ProcessName => Path.GetFileNameWithoutExtension(ExePath);
}

internal static class BrowserLocator
{
    private static readonly string[] PreferOrder = { "Edge", "Chrome", "Brave", "Firefox", "Opera" };

    /// <summary>Todos los navegadores compatibles instalados, en orden de preferencia.</summary>
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

    /// <summary>Nombre del navegador predeterminado de Windows si es compatible (o null).</summary>
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
            catch { /* ignore */ }
        }
        return null;
    }
}

// ---------------------------------------------------------------------------
//  Perfiles temporales
// ---------------------------------------------------------------------------

internal static class Profiles
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "IGP_Soft_Profiles");

    public static void CleanupOld()
    {
        if (!Directory.Exists(Root)) return;
        foreach (string dir in Directory.EnumerateDirectories(Root))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* en uso */ }
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

    // ---- Datos de navegación conservados (cookies, inicios de sesión) ----

    /// <summary>Perfiles que NO se borran: uno por navegador y enlace, junto al ejecutable.</summary>
    public static readonly string KeepRoot = Path.Combine(AppContext.BaseDirectory, "IGP_Soft_Datos");

    public static bool KeepData
    {
        get => Config.Get("conservarDatos") == "1";
        set => Config.Set("conservarDatos", value ? "1" : "0");
    }

    public static bool IsKept(string dir) =>
        dir.StartsWith(KeepRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Carpeta fija para este enlace en este navegador (o null si no se puede crear).
    /// Un enlace no se abre dos veces a la vez, así que la carpeta nunca la comparten dos ventanas.</summary>
    public static string? KeptDir(string browserName, string url)
    {
        try
        {
            string slug = string.Concat(url.Where(char.IsLetterOrDigit).Take(40));
            uint h = 2166136261;   // FNV-1a: estable entre ejecuciones (string.GetHashCode no lo es)
            foreach (char ch in url.ToLowerInvariant()) h = (h ^ ch) * 16777619;
            string d = Path.Combine(KeepRoot, browserName, $"{slug}_{h:x8}");
            Directory.CreateDirectory(d);
            return d;
        }
        catch { return null; }
    }

    public static void DeleteKept()
    {
        try { if (Directory.Exists(KeepRoot)) Directory.Delete(KeepRoot, recursive: true); }
        catch { /* algún navegador aún lo usa */ }
    }
}

// ---------------------------------------------------------------------------
//  Ventana lanzada
// ---------------------------------------------------------------------------

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
}

// ---------------------------------------------------------------------------
//  Lanzar / colocar / cerrar ventanas de navegador
// ---------------------------------------------------------------------------

internal static class ProcessInfo
{
    /// <summary>Línea de comandos de un proceso (o null).</summary>
    public static string? CommandLine(int pid)
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementBaseObject o in s.Get()) return o["CommandLine"] as string;
        }
        catch { /* WMI no disponible */ }
        return null;
    }

    /// <summary>PIDs de procesos <paramref name="name"/> cuya línea de comandos contiene <paramref name="needle"/>.</summary>
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
        catch { /* WMI no disponible */ }
        return pids;
    }
}

/// <summary>Convierte rutas locales a file:/// y arma envoltorios de pantalla completa para medios.</summary>
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
        try { return new Uri(Path.GetFullPath(s)).AbsoluteUri; }
        catch { return s; }
    }

    /// <summary>Si el destino es imagen o video, crea view.html a pantalla completa y devuelve su file:/// URL.</summary>
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

        string path = Path.Combine(dir, "view.html");
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
    // Serializa los lanzamientos para poder identificar con seguridad la ventana nueva.
    private static readonly object LaunchGate = new();

    private static string ToBrowserUrl(string s) => MediaWrapper.ToBrowserUrl(s);

    /// <summary>
    /// Abre <paramref name="url"/> sin bordes y la coloca en el rectángulo <paramref name="area"/>
    /// (una pantalla completa o un segmento de ella), en píxeles físicos.
    /// Devuelve la ventana localizada, o null si no se pudo confirmar.
    /// </summary>
    public static LaunchedWindow? LaunchAndPlace(Browser browser, string url, Rectangle area, string tag,
        IntPtr parentHwnd = default, bool keepData = false)
    {
        lock (LaunchGate)
        {
            string? kept = keepData ? Profiles.KeptDir(browser.Name, url) : null;
            // Un perfil fijo puede seguir en uso por un navegador que quedó abierto (p. ej. tras un
            // cierre inesperado): se cierra antes, si no Firefox se niega a abrir y Chromium reutiliza ese proceso.
            if (kept is not null) KillProfileProcesses(browser.ProcessName, kept);
            string profileDir = kept ?? Profiles.NewDir(tag);
            // Imágenes / videos: se muestran a pantalla completa con un envoltorio HTML.
            // Cualquier otra cosa (URL o página local) se abre tal cual.
            url = MediaWrapper.Prepare(url, profileDir) ?? ToBrowserUrl(url);
            var before = SnapshotWindows(browser.ProcessName);

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
                // --app abre el contenido sin pestañas ni barra de direcciones antes
                // de integrarlo en la superficie contenedora.
                psi.ArgumentList.Add($"--user-data-dir={profileDir}");
                psi.ArgumentList.Add("--no-first-run");
                psi.ArgumentList.Add("--no-default-browser-check");
                psi.ArgumentList.Add("--disable-session-crashed-bubble");
                psi.ArgumentList.Add("--hide-crash-restore-bubble");
                psi.ArgumentList.Add("--disable-infobars");
                psi.ArgumentList.Add("--disable-features=Translate");
                psi.ArgumentList.Add("--disable-search-engine-choice-screen");
                psi.ArgumentList.Add($"--window-position={area.X},{area.Y}");
                psi.ArgumentList.Add($"--window-size={area.Width},{area.Height}");
                psi.ArgumentList.Add($"--app={url}");
            }
            else // Firefox
            {
                // La barra se oculta con userChrome.css antes de integrar la ventana.
                WriteFirefoxProfile(profileDir);
                psi.ArgumentList.Add("-profile");
                psi.ArgumentList.Add(profileDir);
                psi.ArgumentList.Add("-no-remote");
                psi.ArgumentList.Add("-new-instance");
                psi.ArgumentList.Add(url);
            }

            try
            {
                Process? started = Process.Start(psi);
                if (started is not null)
                {
                    // Vaciar las salidas para no bloquear al navegador ni heredar handles de consola.
                    started.OutputDataReceived += static (_, _) => { };
                    started.ErrorDataReceived += static (_, _) => { };
                    started.BeginOutputReadLine();
                    started.BeginErrorReadLine();
                }
            }
            catch
            {
                CleanupProfile(browser.ProcessName, profileDir);
                return null;
            }

            // Localizar la ventana cuyo proceso se lanzó con NUESTRO perfil temporal.
            // Así nunca se confunde con una ventana que el usuario ya tenía abierta.
            // La línea de comandos de un PID no cambia: se consulta (WMI, lento) una sola vez por proceso.
            IntPtr hwnd = IntPtr.Zero;
            int pid = 0;
            var ownPid = new Dictionary<int, bool>();
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 15000 && hwnd == IntPtr.Zero)
            {
                Thread.Sleep(300);
                foreach ((IntPtr h, int p) in EnumBrowserWindows(browser.ProcessName))
                {
                    if (before.Contains(h)) continue;
                    if (!ownPid.TryGetValue(p, out bool ours))
                    {
                        string? cl = ProcessInfo.CommandLine(p);
                        ours = cl is not null && cl.Contains(profileDir, StringComparison.OrdinalIgnoreCase);
                        if (cl is not null) ownPid[p] = ours;   // null: WMI falló, se reintenta
                    }
                    if (ours) { hwnd = h; pid = p; break; }
                }
            }

            if (hwnd == IntPtr.Zero)
            {
                CleanupProfile(browser.ProcessName, profileDir);
                return null;
            }

            // Todas las familias sin bordes: así llenan el rectángulo sin dejar ver el escritorio.
            const bool borderless = true;
            var win = new LaunchedWindow
            {
                ProcessId = pid, Hwnd = hwnd, ProfileDir = profileDir,
                ProcessName = browser.ProcessName, Borderless = borderless,
                ParentHwnd = parentHwnd, DesiredArea = area,
            };

            PlaceWindow(win);
            // Reafirmar posición y "sin bordes" unos segundos (la ventana tiende a re-encajar al abrir).
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

    /// <summary>Recoloca una ventana ya abierta en un nuevo rectángulo (al re-segmentar).</summary>
    public static void Reposition(LaunchedWindow win, Rectangle area)
    {
        try
        {
            lock (win)
            {
                win.DesiredArea = area;
                if (NativeMethods.IsWindow(win.Hwnd))
                    PlaceWindow(win);
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>Vuelve a poner la ventana por encima de todo (incl. la barra de tareas).</summary>
    public static void BringToTop(IntPtr hwnd)
    {
        try
        {
            if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        catch { /* ignore */ }
    }

    public static void Close(LaunchedWindow win)
    {
        win.Closed = true;

        // 1) Cierre ordenado.
        try
        {
            if (win.Hwnd != IntPtr.Zero && NativeMethods.IsWindow(win.Hwnd))
                NativeMethods.PostMessage(win.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* ignore */ }

        Thread.Sleep(900);

        // 2) Forzar el cierre de TODO proceso lanzado con este perfil temporal y limpiar.
        CleanupProfile(win.ProcessName, win.ProfileDir);
    }

    private static void KillProfileProcesses(string processName, string profileDir)
    {
        foreach (int pid in ProcessInfo.ByCommandLineContains(processName, profileDir))
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
            catch { /* ya terminó */ }
        }
    }

    private static void CleanupProfile(string processName, string profileDir)
    {
        KillProfileProcesses(processName, profileDir);
        if (Profiles.IsKept(profileDir)) return;   // datos conservados: la carpeta se queda

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

    // --- helpers Win32 -----------------------------------------------------

    private static void PlaceWindow(LaunchedWindow win)
    {
        lock (win)
        {
            IntPtr hwnd = win.Hwnd;
            Rectangle area = win.DesiredArea;
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            bool frameChanged = false;
            if (win.Borderless)
            {
                // Quita título y borde para que el contenido web llene TODO el rectángulo.
                long cur = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
                long want = cur & ~(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME |
                                    NativeMethods.WS_POPUP);
                if (win.ParentHwnd != IntPtr.Zero) want |= NativeMethods.WS_CHILD;
                if (want != cur)
                {
                    NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(want));
                    frameChanged = true;
                }
            }

            if (win.ParentHwnd != IntPtr.Zero && NativeMethods.GetParent(hwnd) != win.ParentHwnd)
            {
                NativeMethods.SetParent(hwnd, win.ParentHwnd);
                frameChanged = true;
            }

            // Embebida y ya en su sitio: no se toca (la vigilancia periódica no debe provocar
            // re-layouts ni parpadeo en el navegador).
            if (!frameChanged && win.ParentHwnd != IntPtr.Zero &&
                NativeMethods.IsWindowVisible(hwnd) && IsAt(hwnd, win.ParentHwnd, area))
                return;

            // Las ventanas embebidas usan coordenadas relativas a la superficie contenedora.
            // Las ventanas independientes (selftest) conservan el comportamiento anterior.
            uint flags = NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW;
            if (frameChanged) flags |= NativeMethods.SWP_FRAMECHANGED;
            IntPtr order = win.ParentHwnd == IntPtr.Zero ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
            NativeMethods.SetWindowPos(hwnd, order, area.X, area.Y, area.Width, area.Height, flags);
        }
    }

    /// <summary>¿La ventana ocupa exactamente <paramref name="area"/> dentro de su contenedor (sin bordes)?</summary>
    private static bool IsAt(IntPtr hwnd, IntPtr parent, Rectangle area)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r) ||
            !NativeMethods.GetWindowRect(parent, out NativeMethods.RECT p))
            return false;
        return r.Left - p.Left == area.X && r.Top - p.Top == area.Y &&
               r.Right - r.Left == area.Width && r.Bottom - r.Top == area.Height;
    }

    private static void WriteFirefoxProfile(string dir)
    {
        try
        {
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
                // Con datos conservados el perfil se reutiliza: sin «¿restaurar la sesión?» tras un cierre forzado.
                "user_pref(\"browser.sessionstore.resume_from_crash\", false);\n" +
                "user_pref(\"browser.sessionstore.resume_session_once\", false);\n" +
                "user_pref(\"browser.startup.page\", 0);\n" +
                "user_pref(\"toolkit.legacyUserProfileCustomizations.stylesheets\", true);\n" +
                // Permitir que view.html (file://) cargue la imagen/video vecino y autoreproduzca.
                "user_pref(\"security.fileuri.strict_origin_policy\", false);\n" +
                "user_pref(\"media.autoplay.default\", 0);\n" +
                "user_pref(\"media.autoplay.blocking_policy\", 0);\n");

            string chromeDir = Path.Combine(dir, "chrome");
            Directory.CreateDirectory(chromeDir);
            File.WriteAllText(Path.Combine(chromeDir, "userChrome.css"),
                "#navigator-toolbox { visibility: collapse !important; }\n");
        }
        catch { /* si falla, Firefox abre igual (con su barra) */ }
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
            catch { /* proceso terminó */ }

            return true;
        }, IntPtr.Zero);

        return result;
    }
}

// ---------------------------------------------------------------------------
//  Monitores
// ---------------------------------------------------------------------------

internal sealed record MonitorInfo(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary)
{
    public string ShortName => DeviceName.Replace(@"\\.\", "");
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
                    (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        };

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);

        return list
            .OrderBy(m => ExtractIndex(m.DeviceName))
            .ThenBy(m => m.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ExtractIndex(string deviceName)
    {
        var digits = new string(deviceName.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int n) ? n : int.MaxValue;
    }
}

// ---------------------------------------------------------------------------
//  P/Invoke
// ---------------------------------------------------------------------------

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

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_TOP = IntPtr.Zero;
    public const int SW_RESTORE = 9;
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
    public const long WS_POPUP = unchecked((long)0x80000000);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

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
