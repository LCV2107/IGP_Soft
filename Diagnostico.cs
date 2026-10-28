using System.Text;

namespace IGP_Soft;

internal static class Diagnostico
{
    public static readonly string FilePath = Path.Combine(AppPaths.DataDir, "IGP_Soft.diagnostico.log");

    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".anterior.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }

    public static string Awareness(IntPtr hwnd)
    {
        try
        {
            IntPtr ctx = NativeMethods.GetWindowDpiAwarenessContext(hwnd);
            int a = NativeMethods.GetAwarenessFromDpiAwarenessContext(ctx);
            string kind = a switch
            {
                0 => "sin soporte de DPI (Windows la escala como imagen)",
                1 => "DPI del sistema",
                2 => "DPI por monitor",
                _ => "desconocido",
            };
            return $"{kind}, {NativeMethods.GetDpiForWindow(hwnd)} ppp";
        }
        catch { return "desconocido"; }
    }
}
