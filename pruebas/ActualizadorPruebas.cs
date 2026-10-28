namespace IGP_Soft.Pruebas;

public class ActualizadorPruebas
{
    private static SemVer V(string s) => SemVer.Parse(s)!;

    [Theory]
    [InlineData("1.0.1", "1.0.0")]
    [InlineData("1.0.2-exp.1", "1.0.1")]
    [InlineData("1.0.2", "1.0.2-exp.9")]
    [InlineData("1.0.2-exp.10", "1.0.2-exp.9")]
    [InlineData("v2.0.0", "1.9.9")]
    public void Compara_versiones(string mayor, string menor)
    {
        Assert.True(V(mayor).CompareTo(V(menor)) > 0);
        Assert.True(V(menor).CompareTo(V(mayor)) < 0);
    }

    [Fact]
    public void Lee_el_sufijo_experimental()
    {
        SemVer v = V("v1.0.2-exp.3");
        Assert.True(v.IsPrerelease);
        Assert.Equal("1.0.2-exp.3", v.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1")]
    public void Rechaza_versiones_invalidas(string s) => Assert.Null(SemVer.Parse(s));

    private const string Releases = """
    [
      { "draft": false, "prerelease": true, "tag_name": "v1.0.2-exp.1", "name": "IGP_Soft 1.0.2-exp.1", "body": "prueba",
        "published_at": "2026-10-28T20:00:00Z", "html_url": "https://github.com/LCV2107/IGP_Soft/releases/tag/v1.0.2-exp.1",
        "assets": [
          { "name": "IGP_Soft-1.0.2-exp.1.msi", "size": 100, "browser_download_url": "https://github.com/LCV2107/IGP_Soft/releases/download/v1.0.2-exp.1/IGP_Soft-1.0.2-exp.1.msi" },
          { "name": "IGP_Soft-1.0.2-exp.1.msi.sha256", "size": 1, "browser_download_url": "https://github.com/LCV2107/IGP_Soft/releases/download/v1.0.2-exp.1/IGP_Soft-1.0.2-exp.1.msi.sha256" },
          { "name": "IGP_Soft-1.0.2-exp.1-portable.exe", "size": 200, "browser_download_url": "https://github.com/LCV2107/IGP_Soft/releases/download/v1.0.2-exp.1/IGP_Soft-1.0.2-exp.1-portable.exe" },
          { "name": "IGP_Soft-1.0.2-exp.1-portable.exe.sha256", "size": 1, "browser_download_url": "https://github.com/LCV2107/IGP_Soft/releases/download/v1.0.2-exp.1/IGP_Soft-1.0.2-exp.1-portable.exe.sha256" }
        ] },
      { "draft": false, "prerelease": false, "tag_name": "v1.0.1", "name": "IGP_Soft 1.0.1", "body": "",
        "published_at": "2026-10-27T20:00:00Z", "html_url": "https://github.com/LCV2107/IGP_Soft/releases/tag/v1.0.1",
        "assets": [
          { "name": "IGP_Soft-1.0.1.msi", "size": 90, "browser_download_url": "https://github.com/LCV2107/IGP_Soft/releases/download/v1.0.1/IGP_Soft-1.0.1.msi" },
          { "name": "IGP_Soft-1.0.1-portable.exe", "size": 190, "browser_download_url": "https://evil.example.com/IGP_Soft-1.0.1-portable.exe" }
        ] }
    ]
    """;

    [Fact]
    public void Instalado_elige_el_msi_con_su_huella()
    {
        UpdateInfo? u = Updater.ParseReleases(Releases, UpdateChannel.Experimental, V("1.0.0"), installed: true);
        Assert.NotNull(u);
        Assert.Equal("IGP_Soft-1.0.2-exp.1.msi", u!.FileName);
        Assert.EndsWith("IGP_Soft-1.0.2-exp.1.msi.sha256", u.ShaUrl);
    }

    [Fact]
    public void Portable_elige_el_exe_con_su_huella()
    {
        UpdateInfo? u = Updater.ParseReleases(Releases, UpdateChannel.Experimental, V("1.0.0"), installed: false);
        Assert.Equal("IGP_Soft-1.0.2-exp.1-portable.exe", u!.FileName);
        Assert.EndsWith("portable.exe.sha256", u.ShaUrl);
    }

    [Fact]
    public void Canal_estable_ignora_las_versiones_de_prueba()
    {
        UpdateInfo? u = Updater.ParseReleases(Releases, UpdateChannel.Stable, V("1.0.0"), installed: true);
        Assert.Equal("1.0.1", u!.Version.ToString());
        Assert.Null(u.ShaUrl);
    }

    [Fact]
    public void Ignora_descargas_fuera_de_github()
    {
        UpdateInfo? u = Updater.ParseReleases(Releases, UpdateChannel.Stable, V("1.0.0"), installed: false);
        Assert.Null(u);
    }

    [Fact]
    public void Sin_novedades_si_ya_se_tiene_la_ultima()
    {
        Assert.Null(Updater.ParseReleases(Releases, UpdateChannel.Experimental, V("1.0.2-exp.1"), installed: true));
    }
}
