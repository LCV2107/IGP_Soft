namespace IGP_Soft.Pruebas;

[Collection("Galeria")]
public sealed class GaleriaPruebas : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), "igp_galeria_" + Guid.NewGuid().ToString("N") + ".json");

    public GaleriaPruebas() => GaleriaCarpetas.Ruta = _ruta;

    public void Dispose()
    {
        try { File.Delete(_ruta); }
        catch { }
    }

    [Fact]
    public void Crea_renombra_y_borra_carpetas_sin_tocar_los_archivos()
    {
        Assert.True(GaleriaCarpetas.Crear("Sismos"));
        Assert.False(GaleriaCarpetas.Crear("  sismos "));
        Assert.True(GaleriaCarpetas.Crear("Volcanes"));
        GaleriaCarpetas.Mover(new[] { @"Assets\a.mp4", "Assets/b.png" }, "Sismos");
        Assert.Equal("Sismos", GaleriaCarpetas.CarpetaDe(@"Assets\b.png"));
        Assert.Equal(2, GaleriaCarpetas.Cantidad("sismos"));

        Assert.True(GaleriaCarpetas.Renombrar("Sismos", "Sismos 2026"));
        Assert.False(GaleriaCarpetas.Renombrar("Volcanes", "SISMOS 2026"));
        Assert.Equal("Sismos 2026", GaleriaCarpetas.CarpetaDe(@"Assets\a.mp4"));

        GaleriaCarpetas.Eliminar("Sismos 2026");
        Assert.Null(GaleriaCarpetas.CarpetaDe(@"Assets\a.mp4"));
        Assert.Equal(new[] { "Volcanes" }, GaleriaCarpetas.Carpetas);
    }

    [Fact]
    public void Se_guarda_en_disco_y_se_vuelve_a_leer()
    {
        GaleriaCarpetas.Crear("Mapas");
        GaleriaCarpetas.Mover(new[] { @"Assets\mapa.png" }, "Mapas");
        GaleriaCarpetas.Recargar();
        Assert.Equal("Mapas", GaleriaCarpetas.CarpetaDe(@"Assets\mapa.png"));
        Assert.True(File.Exists(_ruta));
    }

    [Fact]
    public void Quita_los_archivos_que_ya_no_existen_y_sigue_a_los_renombrados()
    {
        GaleriaCarpetas.Crear("C");
        GaleriaCarpetas.Mover(new[] { @"Assets\uno.png", @"Assets\dos.png" }, "C");
        GaleriaCarpetas.Limpiar(new[] { @"Assets\uno.png" });
        Assert.Null(GaleriaCarpetas.CarpetaDe(@"Assets\dos.png"));
        GaleriaCarpetas.Renombrados(new Dictionary<string, string> { [@"Assets\uno.png"] = @"Assets\uno-1.png" });
        Assert.Equal("C", GaleriaCarpetas.CarpetaDe(@"Assets\uno-1.png"));
        Assert.Null(GaleriaCarpetas.CarpetaDe(@"Assets\uno.png"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Clases de 5.º", true)]
    public void Valida_el_nombre(string nombre, bool valido) =>
        Assert.Equal(valido, GaleriaCarpetas.Validar(nombre) is null);

    [Fact]
    public void Un_nombre_demasiado_largo_no_se_acepta() =>
        Assert.NotNull(GaleriaCarpetas.Validar(new string('x', GaleriaCarpetas.MaxNombre + 1)));

    [Fact]
    public void Mover_a_una_carpeta_inexistente_no_hace_nada()
    {
        GaleriaCarpetas.Mover(new[] { @"Assets\x.png" }, "No existe");
        Assert.Null(GaleriaCarpetas.CarpetaDe(@"Assets\x.png"));
    }

    [Fact]
    public void Un_archivo_danado_empieza_vacio()
    {
        File.WriteAllText(_ruta, "{ no es json");
        GaleriaCarpetas.Recargar();
        Assert.Empty(GaleriaCarpetas.Carpetas);
    }
}
