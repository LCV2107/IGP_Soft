using System.Text.Json;

namespace IGP_Soft.Pruebas;

[Collection("Perfiles")]
public class PerfilesPruebas
{
    private static void Preparar(params LayoutProfile[] perfiles)
    {
        ProfileStore.Remote = _ => { };
        CarpetasPerfiles.Persistir = _ => { };
        CarpetasPerfiles.Fijar(Array.Empty<string>());
        ProfileStore.SetFromRemote(perfiles);
    }

    [Fact]
    public void La_carpeta_viaja_con_el_perfil_y_un_perfil_viejo_no_tiene()
    {
        var p = new LayoutProfile { Name = "Sala", Carpeta = "Turno noche" };
        LayoutProfile leido = JsonSerializer.Deserialize<LayoutProfile>(JsonSerializer.Serialize(p))!;
        Assert.Equal("Turno noche", leido.Carpeta);
        LayoutProfile viejo = JsonSerializer.Deserialize<LayoutProfile>("{\"Name\":\"Sala\",\"Monitors\":[]}")!;
        Assert.Null(viejo.Carpeta);
    }

    [Fact]
    public void Las_carpetas_se_crean_renombran_y_eliminan_sin_borrar_perfiles()
    {
        Preparar(new LayoutProfile { Name = "A" }, new LayoutProfile { Name = "B", Carpeta = "Sismos" }, new LayoutProfile { Name = ProfileStore.LastSession });
        Assert.Equal(new[] { "Sismos" }, CarpetasPerfiles.Todas);

        Assert.True(CarpetasPerfiles.Crear("Volcanes"));
        Assert.False(CarpetasPerfiles.Crear("volcanes"));
        Assert.NotNull(CarpetasPerfiles.Validar("  "));
        Assert.NotNull(CarpetasPerfiles.Validar(new string('x', CarpetasPerfiles.MaxNombre + 1)));
        Assert.Equal(new[] { "Sismos", "Volcanes" }, CarpetasPerfiles.Todas);
        Assert.Equal(0, CarpetasPerfiles.Cantidad("Volcanes"));

        CarpetasPerfiles.Mover(new[] { "A", ProfileStore.LastSession }, "volcanes");
        Assert.Equal("Volcanes", ProfileStore.Get("A")!.Carpeta);
        Assert.Null(ProfileStore.Get(ProfileStore.LastSession)!.Carpeta);
        Assert.Equal(1, CarpetasPerfiles.Cantidad("Volcanes"));

        Assert.True(CarpetasPerfiles.Renombrar("Sismos", "Sismos 2026"));
        Assert.Equal("Sismos 2026", ProfileStore.Get("B")!.Carpeta);
        Assert.False(CarpetasPerfiles.Renombrar("Sismos 2026", "Volcanes"));

        CarpetasPerfiles.Eliminar("Volcanes");
        Assert.Null(ProfileStore.Get("A")!.Carpeta);
        Assert.Equal(3, ProfileStore.Items.Count);
        Assert.Equal(new[] { "Sismos 2026" }, CarpetasPerfiles.Todas);

        CarpetasPerfiles.Mover(new[] { "B" }, null);
        Assert.Null(ProfileStore.Get("B")!.Carpeta);
        Assert.Equal(new[] { "Sismos 2026" }, CarpetasPerfiles.Todas);
        CarpetasPerfiles.Mover(new[] { "B" }, "No existe");
        Assert.Null(ProfileStore.Get("B")!.Carpeta);
    }

    [Fact]
    public void Reemplazar_un_perfil_conserva_su_carpeta()
    {
        Preparar(new LayoutProfile { Name = "Sala", Carpeta = "Sismos" });
        ProfileStore.Put(new LayoutProfile { Name = "Sala" });
        Assert.Equal("Sismos", ProfileStore.Get("Sala")!.Carpeta);
        ProfileStore.Put(new LayoutProfile { Name = ProfileStore.LastSession, Carpeta = "Sismos" });
        Assert.Null(ProfileStore.Get(ProfileStore.LastSession)!.Carpeta);
    }

    [Fact]
    public void Un_perfil_se_guarda_y_se_lee_igual()
    {
        var perfil = new LayoutProfile
        {
            Name = "Sala",
            Browser = "Edge",
            Saved = new DateTime(2026, 10, 28, 10, 0, 0),
            Monitors =
            {
                new MonitorLayout
                {
                    Number = 2, Device = @"\\.\DISPLAY2", Cols = 2, Rows = 2,
                    Layout = new List<ZoneSpec> { new(0, 0, 1, 2), new(1, 0), new(1, 1, Off: true) },
                    Links = { new ZoneLayout { Zone = 1, Titulo = "Último sismo", Url = "https://ultimosismo.igp.gob.pe/ultimo-sismo" } },
                },
            },
        };

        string json = JsonSerializer.Serialize(perfil);
        LayoutProfile leido = JsonSerializer.Deserialize<LayoutProfile>(json)!;

        MonitorLayout m = leido.Monitors.Single();
        Assert.Equal(@"\\.\DISPLAY2", m.Device);
        Assert.Equal(3, m.Layout!.Count);
        Assert.True(m.Layout[2].Off);
        GridLayout g = GridLayout.FromSaved(m.Cols, m.Rows, m.Layout);
        Assert.Equal(2, g.ActiveCount);
        Assert.Equal(1, leido.LinkCount);
    }

    [Fact]
    public void Los_monitores_omitidos_no_cuentan_enlaces()
    {
        var perfil = new LayoutProfile
        {
            Monitors =
            {
                new MonitorLayout { Excluded = true, Links = { new ZoneLayout { Url = "https://a" } } },
                new MonitorLayout { Links = { new ZoneLayout { Url = "https://b" } } },
            },
        };
        Assert.Equal(1, perfil.LinkCount);
    }
}
