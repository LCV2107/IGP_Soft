using System.Runtime.InteropServices;

namespace IGP_Soft;

internal enum MediaKind { Image, Video, Other }

internal static class Thumbnails
{
    private static readonly string[] GdiImages = { ".jpg", ".jpeg", ".jfif", ".png", ".gif", ".bmp", ".tif", ".tiff", ".ico" };
    private static readonly string[] OtherImages = { ".webp", ".avif", ".svg", ".heic", ".apng" };
    private static readonly string[] Videos = { ".mp4", ".webm", ".ogv", ".ogg", ".mov", ".m4v", ".mkv", ".avi", ".wmv" };

    public static MediaKind KindOf(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (GdiImages.Contains(ext) || OtherImages.Contains(ext)) return MediaKind.Image;
        if (Videos.Contains(ext)) return MediaKind.Video;
        return MediaKind.Other;
    }

    public static bool CanLoadDirectly(string path) => GdiImages.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static Image? LoadImage(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Image.FromStream(new MemoryStream(bytes));
        }
        catch { return null; }
    }

    public static Bitmap? FromShell(string path, int size)
    {
        try
        {
            Guid iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory factory);
            try
            {
                if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out IntPtr hbmp) != 0 ||
                    hbmp == IntPtr.Zero)
                    return null;
                try { return Image.FromHbitmap(hbmp); }
                finally { NativeMethods.DeleteObject(hbmp); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch { return null; }
    }

    public static Image? Best(string path, int size)
    {
        if (CanLoadDirectly(path)) return LoadImage(path) ?? FromShell(path, size);
        return FromShell(path, size);
    }

    public static Bitmap Compose(Image? source, string path, Size box)
    {
        var bmp = new Bitmap(box.Width, box.Height);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(236, 238, 241));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (source is not null)
        {
            double scale = Math.Min((double)box.Width / source.Width, (double)box.Height / source.Height);
            int w = Math.Max(1, (int)(source.Width * scale)), h = Math.Max(1, (int)(source.Height * scale));
            g.DrawImage(source, (box.Width - w) / 2, (box.Height - h) / 2, w, h);
        }
        else
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            using var f = new Font("Segoe UI", 16f, FontStyle.Bold);
            TextRenderer.DrawText(g, ext.Length == 0 ? "?" : ext, f, new Rectangle(Point.Empty, box), Color.FromArgb(120, 130, 140),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (KindOf(path) == MediaKind.Video)
        {
            var badge = new Rectangle(box.Width - 30, box.Height - 26, 24, 20);
            using var bb = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
            g.FillRectangle(bb, badge);
            using var vf = new Font("Segoe UI", 9f, FontStyle.Bold);
            TextRenderer.DrawText(g, "▶", vf, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        return bmp;
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_THUMBNAILONLY = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
}
