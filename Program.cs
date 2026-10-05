using System.Threading;

namespace IGP_Soft;

internal static class Program
{
    internal const string MutexName = @"Local\IGP_Soft.SingleInstance.v1";
    internal const string ShowEventName = @"Local\IGP_Soft.ShowPanel.v1";

    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        LinkStore.Load();
        Config.Load();
        ProfileStore.Load();

        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            string? extra = args.FirstOrDefault(a => !a.StartsWith("--"));
            string? brw = args.FirstOrDefault(a => a.StartsWith("--browser="))?["--browser=".Length..];
            SelfTest.Run(extra, brw);
            return;
        }

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isNew);
        if (!isNew)
        {
            try { using var ev = EventWaitHandle.OpenExisting(ShowEventName); ev.Set(); }
            catch { }
            return;
        }

        string? perfil = args.FirstOrDefault(a => a.StartsWith("--perfil=", StringComparison.OrdinalIgnoreCase))
                             ?["--perfil=".Length..].Trim('"');

        AppDomain.CurrentDomain.UnhandledException += (_, _) => WindowManager.ReleaseAll();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WindowManager.ReleaseAll();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(perfil ?? Startup.Profile));
        GC.KeepAlive(_instanceMutex);
    }
}

internal static class SelfTest
{
    public static void Run(string? extra = null, string? browserName = null)
    {
        Profiles.CleanupOld();

        var installed = BrowserLocator.InstalledAll();
        Browser? b = browserName is not null
            ? installed.FirstOrDefault(x => x.Name.Equals(browserName, StringComparison.OrdinalIgnoreCase))
            : null;
        b ??= BrowserLocator.Resolve(out _);
        if (b is null) { Console.WriteLine("Sin navegador compatible."); return; }
        Console.WriteLine($"Navegador: {b.Name}");

        List<MonitorInfo> mons = MonitorEnumerator.GetOrdered();
        Console.WriteLine($"Monitores: {mons.Count}");

        var abiertas = new List<(LaunchedWindow W, System.Drawing.Rectangle A)>();
        for (int i = 0; i < mons.Count && i < LinkStore.Links.Count; i++)
        {
            MonitorInfo m = mons[i];
            string target = LinkStore.Links[i].Url;
            var area = new System.Drawing.Rectangle(m.X, m.Y, m.Width, m.Height);
            if (i == 0 && extra == "seg")  area = new System.Drawing.Rectangle(m.X, m.Y, m.Width / 2, m.Height / 2);
            if (i == 0 && extra == "seg2") area = new System.Drawing.Rectangle(m.X, m.Y + m.Height / 2, m.Width / 2, m.Height - m.Height / 2);
            Console.WriteLine($"\n[{m.ShortName}] objetivo {area} -> {target}");
            LaunchedWindow? w = WindowManager.LaunchAndPlace(b, target, area, m.ShortName + i);
            if (w is null) { Console.WriteLine("  NO se localizó la ventana."); continue; }
            abiertas.Add((w, area));
            Thread.Sleep(2500);
            if (NativeMethods.GetWindowRect(w.Hwnd, out NativeMethods.RECT r))
            {
                bool ok = Math.Abs(r.Left - area.X) <= 40 && Math.Abs(r.Top - area.Y) <= 40
                          && Math.Abs((r.Right - r.Left) - area.Width) <= 40
                          && Math.Abs((r.Bottom - r.Top) - area.Height) <= 40;
                Console.WriteLine($"  ventana en ({r.Left},{r.Top}) {r.Right - r.Left}x{r.Bottom - r.Top}  => {(ok ? "OK" : "FUERA DE SITIO")}");
            }
        }

        if (extra is "seg2" or "hold")
        {
            Console.WriteLine("Manteniendo 15 s (revisa que no se vea el escritorio ni la barra de tareas)...");
            for (int k = 0; k < 30; k++)
            {
                foreach ((LaunchedWindow w, System.Drawing.Rectangle a) in abiertas)
                    WindowManager.Reposition(w, a);
                Thread.Sleep(500);
            }
        }

        Console.WriteLine("\nCerrando en 4 s...");
        Thread.Sleep(4000);
        foreach ((LaunchedWindow w, _) in abiertas) WindowManager.Close(w);
        Console.WriteLine("Listo.");
    }
}
