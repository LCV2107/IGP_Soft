using System.Text.Json;

namespace IGP_Soft.Pruebas;

public sealed class ZoomPruebas
{
    private static readonly SizeF Img = new(2000, 1000);
    private static readonly SizeF Caja = new(1000, 1000);

    [Fact]
    public void Encajada_centra_la_imagen_completa()
    {
        RectangleF r = Encuadre.Destino(VistaImagen.Encajada, Img, Caja);
        Assert.Equal(0, r.X, 3);
        Assert.Equal(250, r.Y, 3);
        Assert.Equal(1000, r.Width, 3);
        Assert.Equal(500, r.Height, 3);
    }

    [Fact]
    public void El_zoom_va_hacia_el_punto_del_raton_y_lo_deja_quieto()
    {
        var punto = new PointF(800, 450);
        RectangleF antes = Encuadre.Destino(VistaImagen.Encajada, Img, Caja);
        double ux = (punto.X - antes.X) / antes.Width, uy = (punto.Y - antes.Y) / antes.Height;
        VistaImagen v = Encuadre.ZoomHacia(VistaImagen.Encajada, 3, punto, Img, Caja);
        RectangleF despues = Encuadre.Destino(v, Img, Caja);
        Assert.Equal(3, v.Zoom, 3);
        Assert.Equal(punto.X, despues.X + ux * despues.Width, 1);
        Assert.Equal(punto.Y, despues.Y + uy * despues.Height, 1);
    }

    [Fact]
    public void El_zoom_queda_entre_100_y_1000_y_no_deja_bordes_vacios()
    {
        VistaImagen v = Encuadre.ZoomHacia(VistaImagen.Encajada, 0.5, new PointF(500, 500), Img, Caja);
        Assert.True(v.EsEncajada);
        v = Encuadre.ZoomHacia(VistaImagen.Encajada, 50, new PointF(500, 500), Img, Caja);
        Assert.Equal(VistaImagen.ZoomMaximo, v.Zoom, 3);
        v = Encuadre.Mover(v, 100000, 0, Img, Caja);
        RectangleF r = Encuadre.Destino(v, Img, Caja);
        Assert.Equal(0, r.X, 1);
    }

    [Fact]
    public void Mover_desplaza_la_vista_al_lado_contrario()
    {
        VistaImagen v = Encuadre.Aplicar(VistaImagen.Encajada, "acercar", Img, Caja);
        VistaImagen d = Encuadre.Aplicar(v, "derecha", Img, Caja);
        VistaImagen i = Encuadre.Aplicar(v, "izquierda", Img, Caja);
        Assert.True(d.X > v.X);
        Assert.True(i.X < v.X);
        Assert.Equal(VistaImagen.Encajada, Encuadre.Aplicar(d, "encajar", Img, Caja));
    }

    [Theory]
    [InlineData("IGP_Soft · vista 2.5000 0.2500 0.7500", 2.5, 0.25, 0.75)]
    [InlineData("IGP_Soft · vista 40 2 -1", 10, 1, -1)]
    public void La_pagina_de_la_imagen_informa_la_vista_en_el_titulo(string titulo, double z, double x, double y)
    {
        VistaImagen? v = Encuadre.LeerTitulo(titulo + " - Microsoft Edge");
        if (y < 0) { Assert.Null(v); return; }
        Assert.Equal(new VistaImagen(z, x, y), v);
    }

    [Fact]
    public void La_pagina_de_la_imagen_arranca_con_la_vista_guardada()
    {
        string html = MediaWrapper.ImagenHtml("file:///C:/a.png", new VistaImagen(3, 0.25, 0.5));
        Assert.Contains("var v={z:3,x:0.25,y:0.5}", html);
        Assert.Contains("IGP_Soft · vista ", html);
        Assert.Contains("src=\"file:///C:/a.png\"", html);
    }

    [Fact]
    public void La_vista_viaja_en_el_perfil_y_un_perfil_viejo_no_la_tiene()
    {
        var z = new ZoneLayout { Zone = 1, Url = @"Assets.png", Vista = new VistaImagen(2, 0.3, 0.6) };
        string json = JsonSerializer.Serialize(z);
        Assert.DoesNotContain("Porcentaje", json);
        Assert.Equal(z.Vista, JsonSerializer.Deserialize<ZoneLayout>(json)!.Vista);
        Assert.Null(JsonSerializer.Deserialize<ZoneLayout>("{\"Zone\":1,\"Url\":\"x.png\"}")!.Vista);
        Assert.Null(VistaImagen.ParaGuardar(VistaImagen.Encajada));
    }

    [Theory]
    [InlineData("acercar", true)]
    [InlineData("abajo", true)]
    [InlineData("borrar", false)]
    [InlineData(null, false)]
    public void Solo_se_aceptan_acciones_de_zoom_conocidas(string? accion, bool valida) =>
        Assert.Equal(valida, Encuadre.EsAccion(accion));
}
