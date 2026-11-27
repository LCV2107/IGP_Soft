namespace IGP_Soft.Pruebas;

public sealed class EscrituraPruebas
{
    [Fact]
    public void El_texto_se_convierte_en_teclas_unicode_tab_y_enter()
    {
        NativeMethods.INPUT[] e = EscrituraZona.Entradas("añ\tb\r\n");
        Assert.Equal(10, e.Length);
        Assert.Equal('a', (char)e[0].ki.wScan);
        Assert.Equal(NativeMethods.KEYEVENTF_UNICODE, e[0].ki.dwFlags);
        Assert.Equal(NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP, e[1].ki.dwFlags);
        Assert.Equal('ñ', (char)e[2].ki.wScan);
        Assert.Equal(0x09, e[4].ki.wVk);
        Assert.Equal(0x0D, e[8].ki.wVk);
        Assert.Equal(NativeMethods.KEYEVENTF_KEYUP, e[9].ki.dwFlags);
        Assert.All(e, x => Assert.Equal(1u, x.type));
        Assert.Equal(Environment.Is64BitProcess ? 40 : 28, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [Fact]
    public void Una_ventana_que_se_mueve_sola_muchas_veces_deja_de_forzarse()
    {
        var w = new LaunchedWindow { ProcessId = 1, Hwnd = IntPtr.Zero, ProfileDir = "", ProcessName = "x", Borderless = false };
        var t = new DateTime(2026, 11, 27, 10, 0, 0);
        for (int i = 0; i < 6; i++) Assert.False(WindowManager.Pelea(w, t.AddSeconds(i)));
        Assert.True(WindowManager.Pelea(w, t.AddSeconds(6)));
        Assert.False(WindowManager.Pelea(w, t.AddSeconds(30)));
    }

    [Fact]
    public void Las_capturas_de_enlaces_son_solo_de_webs_y_ventanas()
    {
        string antes = CapturasEnlaces.Carpeta;
        CapturasEnlaces.Carpeta = Path.Combine(Path.GetTempPath(), "igp_enlaces_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(CapturasEnlaces.Existente("https://a.com/x"));
            CapturasEnlaces.Guardar("https://a.com/x", new byte[] { 1, 2, 3 });
            Assert.NotNull(CapturasEnlaces.Existente("https://A.com/x "));
            Assert.Null(CapturasEnlaces.Existente(@"Assets\a.png"));
            Assert.Null(CapturasEnlaces.Existente(null));
        }
        finally
        {
            try { Directory.Delete(CapturasEnlaces.Carpeta, true); } catch { }
            CapturasEnlaces.Carpeta = antes;
        }
    }
}
