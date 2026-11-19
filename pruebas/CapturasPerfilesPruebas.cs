namespace IGP_Soft.Pruebas;

[Collection("Perfiles")]
public sealed class CapturasPerfilesPruebas : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "igp_capturas_" + Guid.NewGuid().ToString("N"));
    private readonly string _antes = CapturasPerfiles.Carpeta;

    public CapturasPerfilesPruebas() => CapturasPerfiles.Carpeta = _dir;

    public void Dispose()
    {
        CapturasPerfiles.Carpeta = _antes;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static byte[] Jpeg(int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
        return ms.ToArray();
    }

    [Fact]
    public void La_captura_se_guarda_se_lee_y_se_borra_por_perfil()
    {
        Assert.False(CapturasPerfiles.Tiene("Sala: noche/1"));
        CapturasPerfiles.Guardar("Sala: noche/1", new[] { (1, Jpeg(64, 36)), (3, Jpeg(128, 36)) });
        IReadOnlyDictionary<int, Image> c = CapturasPerfiles.De("sala: NOCHE/1");
        Assert.Equal(new[] { 1, 3 }, c.Keys.OrderBy(k => k));
        Assert.Equal(128, c[3].Width);
        Assert.False(CapturasPerfiles.Tiene("Sala: noche/2"));

        CapturasPerfiles.Guardar("Sala: noche/1", new[] { (2, Jpeg(64, 36)) });
        Assert.Equal(new[] { 2 }, CapturasPerfiles.De("Sala: noche/1").Keys);

        CapturasPerfiles.Borrar("Sala: noche/1");
        Assert.False(CapturasPerfiles.Tiene("Sala: noche/1"));
    }
}
