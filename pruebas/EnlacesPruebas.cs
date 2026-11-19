namespace IGP_Soft.Pruebas;

[Collection("Enlaces")]
public sealed class EnlacesPruebas : IDisposable
{
    private readonly List<LinkDef> _antes = LinkStore.Links.ToList();

    public EnlacesPruebas()
    {
        LinkStore.Remote = _ => { };
        LinkStore.SetFromRemote(Array.Empty<LinkDef>());
    }

    public void Dispose()
    {
        LinkStore.SetFromRemote(_antes);
        LinkStore.Remote = null;
    }

    [Fact]
    public void Agregar_la_misma_direccion_devuelve_el_enlace_que_ya_esta()
    {
        LinkDef a = LinkStore.Add(new LinkDef("Video sala", @"Assets\sala.mp4"));
        LinkDef b = LinkStore.Add(new LinkDef("Otro nombre", @"Assets/sala.mp4"));
        LinkDef c = LinkStore.Add(new LinkDef("IGP", "https://www.igp.gob.pe/"));
        LinkDef d = LinkStore.Add(new LinkDef("IGP 2", "https://www.igp.gob.pe"));
        Assert.Equal(a, b);
        Assert.Equal(c, d);
        Assert.Equal(2, LinkStore.Links.Count);
        Assert.DoesNotContain(LinkStore.Links, l => l.Titulo.EndsWith("(2)"));
    }

    [Fact]
    public void Reemplazar_la_lista_quita_las_direcciones_repetidas()
    {
        LinkStore.Replace(new[]
        {
            new LinkDef("Sismo", "https://ultimosismo.igp.gob.pe/ultimo-sismo"),
            new LinkDef("Sismo (2)", "https://ultimosismo.igp.gob.pe/ultimo-sismo"),
            new LinkDef("Sismo", "https://ultimosismo.igp.gob.pe/reportes"),
        });
        Assert.Equal(2, LinkStore.Links.Count);
        Assert.Equal("Sismo", LinkStore.Links[0].Titulo);
        Assert.Equal("Sismo (2)", LinkStore.Links[1].Titulo);
        Assert.Equal("https://ultimosismo.igp.gob.pe/reportes", LinkStore.Links[1].Url);
    }

    [Theory]
    [InlineData(@"Assets\a.png", "Assets/a.png", true)]
    [InlineData("https://a.pe/x/", "https://a.pe/x", true)]
    [InlineData("https://a.pe/x", "https://a.pe/y", false)]
    public void Find_compara_direcciones_equivalentes(string guardada, string buscada, bool encuentra)
    {
        var lista = new List<LinkDef> { new("t", guardada) };
        Assert.Equal(encuentra, LinkStore.Find(buscada, lista) is not null);
    }
}
