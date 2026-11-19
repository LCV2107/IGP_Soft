namespace IGP_Soft;

internal static class NativeImage
{
    public static bool Supports(string url)
    {
        if (url.Contains("://")) return false;
        string path = AssetStore.Resolve(url);
        return Thumbnails.CanLoadDirectly(path) && File.Exists(path);
    }

    public static LaunchedWindow? Show(Control surface, string url, Rectangle area, VistaImagen? vista = null, Action? cambio = null)
    {
        Image? image = Thumbnails.LoadImage(AssetStore.Resolve(url));
        if (image is null) return null;

        var box = new VisorImagen(image, vista) { Bounds = area };
        if (cambio is not null) box.VistaCambiada += cambio;
        surface.Controls.Add(box);
        box.BringToFront();
        box.CreateControl();
        return new LaunchedWindow
        {
            ProcessId = Environment.ProcessId, Hwnd = box.Handle, ProfileDir = "", ProcessName = "",
            Borderless = false, ParentHwnd = surface.Handle, DesiredArea = area, Native = true, Tag = box,
        };
    }

    public static void Dispose(LaunchedWindow window)
    {
        window.Closed = true;
        if (window.Tag is VisorPdf pdf) { pdf.Dispose(); return; }
        if (window.Tag is not VisorImagen box) return;
        Image image = box.Imagen;
        box.Dispose();
        image.Dispose();
    }
}
