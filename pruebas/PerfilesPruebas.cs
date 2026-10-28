using System.Text.Json;

namespace IGP_Soft.Pruebas;

public class PerfilesPruebas
{
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
