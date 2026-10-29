using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IGP_Soft;

internal static class TiposPermitidos
{
    public const int Cabecera = 4096;

    public const string Filtro = "Imágenes, videos, PDF, Word y PowerPoint|*.jpg;*.jpeg;*.jfif;*.png;*.gif;*.webp;*.bmp;*.avif;*.svg;" +
                                 "*.mp4;*.m4v;*.mov;*.webm;*.mkv;*.ogv;*.ogg;*.pdf;*.docx;*.pptx;*.ppsx";

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".jfif"] = "image/jpeg",
        [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp",
        [".avif"] = "image/avif", [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/quicktime",
        [".webm"] = "video/webm", [".mkv"] = "video/x-matroska", [".ogv"] = "video/ogg", [".ogg"] = "video/ogg",
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".ppsx"] = "application/vnd.openxmlformats-officedocument.presentationml.slideshow",
    };

    private static readonly string[] Macros = { "macroenabled", "vbaproject", "vbadata", "activex" };

    private static readonly string[] CajasMov = { "ftyp", "moov", "mdat", "wide", "free", "skip", "pnot" };

    public static bool Extension(string nombre)
    {
        try { return Mime.ContainsKey(Path.GetExtension(nombre)); }
        catch { return false; }
    }

    public static string? TipoMime(string nombre) => Extension(nombre) ? Mime[Path.GetExtension(nombre)] : null;

    public static bool Contenido(string nombre, ReadOnlySpan<byte> c) => Path.GetExtension(nombre).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" => Empieza(c, 0xFF, 0xD8, 0xFF),
        ".png" => Empieza(c, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
        ".gif" => Ascii(c, 0, "GIF87a") || Ascii(c, 0, "GIF89a"),
        ".webp" => Ascii(c, 0, "RIFF") && Ascii(c, 8, "WEBP"),
        ".bmp" => Ascii(c, 0, "BM"),
        ".avif" or ".mp4" or ".m4v" => Ascii(c, 4, "ftyp"),
        ".mov" => c.Length >= 8 && Array.IndexOf(CajasMov, Encoding.ASCII.GetString(c.Slice(4, 4))) >= 0,
        ".webm" or ".mkv" => Empieza(c, 0x1A, 0x45, 0xDF, 0xA3),
        ".ogg" or ".ogv" => Ascii(c, 0, "OggS"),
        ".pdf" => c[..Math.Min(c.Length, 1024)].IndexOf("%PDF-"u8) >= 0,
        ".svg" => EsSvg(c),
        ".docx" or ".pptx" or ".ppsx" => Empieza(c, 0x50, 0x4B, 0x03, 0x04),
        _ => false,
    };

    public static bool Archivo(string ruta, string? nombre = null)
    {
        nombre ??= ruta;
        if (!Extension(nombre)) return false;
        try
        {
            using FileStream fs = File.OpenRead(ruta);
            byte[] c = new byte[Cabecera];
            int n = fs.ReadAtLeast(c, c.Length, throwOnEndOfStream: false);
            if (n == 0 || !Contenido(nombre, c.AsSpan(0, n))) return false;
            fs.Position = 0;
            return Path.GetExtension(nombre).ToLowerInvariant() switch
            {
                ".docx" => OfficeSinMacros(fs, "word/document.xml"),
                ".pptx" or ".ppsx" => OfficeSinMacros(fs, "ppt/presentation.xml"),
                _ => true,
            };
        }
        catch { return false; }
    }

    private static bool OfficeSinMacros(Stream s, string principal)
    {
        using var zip = new System.IO.Compression.ZipArchive(s, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        bool tipos = false, cuerpo = false;
        foreach (System.IO.Compression.ZipArchiveEntry e in zip.Entries)
        {
            string n = e.FullName.Replace('\\', '/');
            if (Macros.Any(m => n.Contains(m, StringComparison.OrdinalIgnoreCase))) return false;
            if (n.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            {
                tipos = true;
                using var r = new StreamReader(e.Open());
                string xml = r.ReadToEnd();
                if (Macros.Any(m => xml.Contains(m, StringComparison.OrdinalIgnoreCase))) return false;
            }
            if (n.Equals(principal, StringComparison.OrdinalIgnoreCase)) cuerpo = true;
        }
        return tipos && cuerpo;
    }

    public static string NombreSeguro(string nombre)
    {
        string solo = nombre.Replace('\\', '/');
        solo = solo[(solo.LastIndexOf('/') + 1)..];
        string ext = Path.GetExtension(solo).ToLowerInvariant();
        string baseName = Path.GetFileNameWithoutExtension(solo).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char ch in baseName)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            bool valido = ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '.';
            char c = valido ? ch : '-';
            if (c == '-' && sb.Length > 0 && sb[^1] == '-') continue;
            sb.Append(c);
        }
        string limpio = sb.ToString().Trim('-', '.');
        while (limpio.Contains("..")) limpio = limpio.Replace("..", ".");
        if (limpio.Length > 60) limpio = limpio[..60].Trim('-', '.');
        if (limpio.Length == 0) limpio = "archivo";
        return limpio + ext;
    }

    private static bool Empieza(ReadOnlySpan<byte> c, params byte[] firma) => c.StartsWith(firma);

    private static bool Ascii(ReadOnlySpan<byte> c, int desde, string texto) =>
        c.Length >= desde + texto.Length && c.Slice(desde, texto.Length).SequenceEqual(Encoding.ASCII.GetBytes(texto));

    private static bool EsSvg(ReadOnlySpan<byte> c)
    {
        string t = Encoding.UTF8.GetString(c[..Math.Min(c.Length, Cabecera)]).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return t.StartsWith('<') && t.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class Huellas
{
    private static readonly object Gate = new();
    private static Dictionary<string, Entrada>? _cache;

    public static string? Ruta { get; set; } = Path.Combine(AppPaths.DataDir, "IGP_Soft.huellas.json");

    private sealed record Entrada(long Bytes, long Fecha, string Sha);

    public static bool Valida(string? sha) => sha is { Length: 64 } && sha.All(Uri.IsHexDigit);

    public static string De(string ruta, CancellationToken ct = default)
    {
        var info = new FileInfo(ruta);
        string clave = Path.GetFullPath(ruta);
        lock (Gate)
        {
            if (Cache().TryGetValue(clave, out Entrada? e) && e.Bytes == info.Length && e.Fecha == info.LastWriteTimeUtc.Ticks)
                return e.Sha;
        }
        string sha = Calcular(ruta, ct);
        Recordar(ruta, sha);
        return sha;
    }

    public static string Calcular(string ruta, CancellationToken ct = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using FileStream fs = new(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
        byte[] buf = new byte[1024 * 1024];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buf, 0, n);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string? Buscar(string carpeta, string sha, long bytes)
    {
        if (!Directory.Exists(carpeta)) return null;
        IEnumerable<string> candidatos;
        try
        {
            candidatos = Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories)
                .Where(f => TiposPermitidos.Extension(f) && new FileInfo(f).Length == bytes)
                .OrderBy(f => Path.GetFileName(f).StartsWith(sha[..6], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }
        catch { return null; }
        foreach (string f in candidatos)
        {
            try { if (string.Equals(De(f), sha, StringComparison.OrdinalIgnoreCase)) return f; }
            catch { }
        }
        return null;
    }

    public static void Recordar(string ruta, string sha)
    {
        try
        {
            var info = new FileInfo(ruta);
            lock (Gate)
            {
                Cache()[Path.GetFullPath(ruta)] = new Entrada(info.Length, info.LastWriteTimeUtc.Ticks, sha.ToLowerInvariant());
                Guardar();
            }
        }
        catch { }
    }

    public static void Olvidar(string ruta)
    {
        lock (Gate)
        {
            if (Cache().Remove(Path.GetFullPath(ruta))) Guardar();
        }
    }

    private static Dictionary<string, Entrada> Cache()
    {
        if (_cache is not null) return _cache;
        _cache = new Dictionary<string, Entrada>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Ruta is not null && File.Exists(Ruta))
                foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, Entrada>>(File.ReadAllText(Ruta)) ?? new())
                    _cache[kv.Key] = kv.Value;
        }
        catch { }
        return _cache;
    }

    private static void Guardar()
    {
        if (Ruta is null || _cache is null) return;
        try
        {
            foreach (string viejo in _cache.Where(kv => !File.Exists(kv.Key)).Select(kv => kv.Key).ToList()) _cache.Remove(viejo);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(_cache));
        }
        catch { }
    }
}

internal static class Imagenes
{
    public static byte[] Jpeg(Image img, long calidad = 70)
    {
        using var ms = new MemoryStream();
        ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var prms = new EncoderParameters(1);
        prms.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, calidad);
        img.Save(ms, codec, prms);
        return ms.ToArray();
    }

    public static Task<byte[]?> MiniaturaJpeg(string ruta, int ancho)
    {
        var tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hilo = new Thread(() =>
        {
            try
            {
                using Image? src = Thumbnails.Best(ruta, ancho);
                if (src is null) { tcs.TrySetResult(null); return; }
                double escala = Math.Min(1.0, (double)ancho / Math.Max(src.Width, src.Height));
                int w = Math.Max(1, (int)(src.Width * escala)), h = Math.Max(1, (int)(src.Height * escala));
                using var bmp = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, w, h);
                }
                tcs.TrySetResult(Jpeg(bmp));
            }
            catch { tcs.TrySetResult(null); }
        }) { IsBackground = true };
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        return tcs.Task;
    }
}

internal sealed record ArchivoRemoto(string Url, string Nombre, long Bytes, DateTime Fecha, int Usos, bool Reserva);

internal interface IArchivosRemotos
{
    Task<(Resultado R, string? Url)> Subir(string ruta, IProgress<double>? progreso, CancellationToken ct);
    Task<List<ArchivoRemoto>?> Archivos();
    Task<Resultado> BorrarArchivo(string url);
    Task<byte[]?> Miniatura(string url, int ancho);
}

internal sealed class RecepcionArchivos : IDisposable
{
    public const int Trozo = 256 * 1024;
    public const string Carpeta = "Remoto";
    public const string ClaveLimite = "LimiteArchivoRemotoMB";
    public const long LimitePorDefectoMB = 2048;
    private const long Margen = 200L * 1024 * 1024;
    private const int MaxLista = 2000;

    private readonly string _assets;
    private readonly string _temporal;
    private readonly Func<long> _limite;
    private readonly Func<long> _libre;
    private readonly object _gate = new();
    private Subida? _actual;
    private int _siguiente;

    private sealed class Subida
    {
        public required string Id { get; init; }
        public required string Sha { get; init; }
        public required string Nombre { get; init; }
        public required long Bytes { get; init; }
        public required string Ruta { get; init; }
        public long Recibido { get; set; }
        public FileStream? Stream { get; set; }
        public IncrementalHash? Hash { get; set; }
        public DateTime Ultimo { get; set; } = DateTime.UtcNow;
    }

    public RecepcionArchivos(string assets, string temporal, Func<long> limite, Func<long>? libre = null)
    {
        _assets = Path.GetFullPath(assets);
        _temporal = temporal;
        _limite = limite;
        _libre = libre ?? EspacioLibre;
    }

    public static RecepcionArchivos DelEquipo() =>
        new(AssetStore.Root, Path.Combine(AppPaths.DataDir, "IGP_Soft.subidas"), () => LimiteMB() * 1024 * 1024);

    public static long LimiteMB() =>
        long.TryParse(Config.Get(ClaveLimite), out long mb) && mb > 0 ? mb : LimitePorDefectoMB;

    public string Reserva => Path.Combine(_assets, Carpeta);

    private long EspacioLibre()
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_temporal))!).AvailableFreeSpace; }
        catch { return long.MaxValue; }
    }

    private string Url(string full) => Path.Combine(AssetStore.FolderName, Path.GetRelativePath(_assets, full));

    private string? Resolver(string? url)
    {
        if (url is null || !AssetStore.IsAssetPath(url) || !TiposPermitidos.Extension(url)) return null;
        string rel = url.Replace('/', '\\');
        if (rel.Split('\\').Any(p => p == "..")) return null;
        try
        {
            string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_assets)!, rel));
            return full.StartsWith(_assets + "\\", StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch { return null; }
    }

    private static string Normalizar(string u) => u.Replace('/', '\\');

    private static JsonObject Ok(long id) => new() { ["id"] = id, ["ok"] = true };

    private static JsonObject Falla(long id, string codigo, string? mensaje = null)
    {
        var o = new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = codigo };
        if (mensaje is not null) o["mensaje"] = mensaje;
        return o;
    }

    private string RutaParcial(string sha) => Path.Combine(_temporal, sha + ".parcial");

    private long Parcial(string sha)
    {
        try
        {
            string r = RutaParcial(sha);
            lock (_gate) if (_actual?.Sha == sha) return _actual.Recibido;
            return File.Exists(r) ? new FileInfo(r).Length : 0;
        }
        catch { return 0; }
    }

    public JsonObject Tengo(long id, JsonElement m)
    {
        string? sha = Protocolo.Texto(m, "sha256")?.ToLowerInvariant();
        long bytes = Protocolo.Numero(m, "bytes") ?? 0;
        if (!Huellas.Valida(sha) || bytes <= 0) return Falla(id, "mensaje_invalido");
        JsonObject r = Ok(id);
        if (Huellas.Buscar(_assets, sha!, bytes) is string ya)
        {
            r["tiene"] = true;
            r["url"] = Url(ya);
            return r;
        }
        r["tiene"] = false;
        r["recibido"] = Parcial(sha!);
        return r;
    }

    public JsonObject Inicio(long id, JsonElement m)
    {
        string? sha = Protocolo.Texto(m, "sha256")?.ToLowerInvariant();
        string nombre = Protocolo.Texto(m, "nombre") ?? "";
        long bytes = Protocolo.Numero(m, "bytes") ?? 0;
        if (!Huellas.Valida(sha) || nombre.Length == 0 || bytes <= 0) return Falla(id, "mensaje_invalido");
        if (!TiposPermitidos.Extension(nombre)) return Falla(id, "tipo_no_permitido");
        long limite = _limite();
        if (bytes > limite) return Falla(id, "demasiado_grande", $"El otro equipo acepta archivos de hasta {limite / (1024 * 1024)} MB.");

        if (Huellas.Buscar(_assets, sha!, bytes) is string ya)
        {
            JsonObject r0 = Ok(id);
            r0["tiene"] = true;
            r0["url"] = Url(ya);
            return r0;
        }

        lock (_gate)
        {
            if (_actual is { } otra && otra.Sha != sha)
            {
                if (DateTime.UtcNow - otra.Ultimo < TimeSpan.FromMinutes(2)) return Falla(id, "ocupado");
                Cerrar(otra);
                _actual = null;
            }

            Subida s = _actual ?? new Subida
            {
                Id = "u" + Interlocked.Increment(ref _siguiente),
                Sha = sha!,
                Nombre = nombre,
                Bytes = bytes,
                Ruta = RutaParcial(sha!),
            };
            try
            {
                if (s.Stream is null)
                {
                    Directory.CreateDirectory(_temporal);
                    LimpiarViejos(s.Ruta);
                    long previo = File.Exists(s.Ruta) ? new FileInfo(s.Ruta).Length : 0;
                    if (previo > bytes) previo = 0;
                    if (_libre() < bytes - previo + Margen) return Falla(id, "sin_espacio", "No hay espacio suficiente en el disco del otro equipo.");
                    s.Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    if (previo > 0) previo = HashearPrevio(s, previo);
                    s.Stream = new FileStream(s.Ruta, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                    s.Stream.SetLength(previo);
                    s.Stream.Seek(previo, SeekOrigin.Begin);
                    s.Recibido = previo;
                }
            }
            catch (Exception ex)
            {
                Cerrar(s);
                return Falla(id, "error_interno", ex.Message);
            }
            s.Ultimo = DateTime.UtcNow;
            _actual = s;
            JsonObject r = Ok(id);
            r["tiene"] = false;
            r["subida"] = s.Id;
            r["recibido"] = s.Recibido;
            return r;
        }
    }

    public JsonObject RecibirTrozo(long id, JsonElement m)
    {
        string? subida = Protocolo.Texto(m, "subida");
        long desde = Protocolo.Numero(m, "desde") ?? -1;
        lock (_gate)
        {
            if (_actual is not { } s || s.Id != subida || s.Stream is null) return Falla(id, "subida_no_encontrada");
            if (desde != s.Recibido)
            {
                JsonObject f = Falla(id, "fuera_de_orden");
                f["recibido"] = s.Recibido;
                return f;
            }
            byte[] datos;
            try { datos = Convert.FromBase64String(Protocolo.Texto(m, "datos") ?? ""); }
            catch { return Falla(id, "mensaje_invalido"); }
            if (datos.Length == 0 || datos.Length > Trozo || s.Recibido + datos.Length > s.Bytes) return Falla(id, "trozo_no_valido");
            if (s.Recibido == 0 && !TiposPermitidos.Contenido(s.Nombre, datos.AsSpan(0, Math.Min(datos.Length, TiposPermitidos.Cabecera))))
            {
                Descartar(s);
                return Falla(id, "contenido_no_valido");
            }
            try
            {
                s.Stream.Write(datos);
                s.Hash?.AppendData(datos);
                s.Recibido += datos.Length;
                s.Ultimo = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Descartar(s);
                return Falla(id, ex is IOException && _libre() < Margen ? "sin_espacio" : "error_interno", ex.Message);
            }
            JsonObject r = Ok(id);
            r["recibido"] = s.Recibido;
            return r;
        }
    }

    public JsonObject Fin(long id, JsonElement m)
    {
        string? subida = Protocolo.Texto(m, "subida");
        Subida s;
        bool verificada;
        lock (_gate)
        {
            if (_actual is not { } a || a.Id != subida) return Falla(id, "subida_no_encontrada");
            if (a.Recibido != a.Bytes)
            {
                JsonObject f = Falla(id, "incompleto");
                f["recibido"] = a.Recibido;
                return f;
            }
            s = a;
            string? calculada = s.Hash is null ? null : Convert.ToHexString(s.Hash.GetHashAndReset()).ToLowerInvariant();
            verificada = calculada is not null;
            Cerrar(s);
            _actual = null;
            if (calculada is not null && calculada != s.Sha)
            {
                Borrar(s.Ruta);
                return Falla(id, "huella_distinta");
            }
        }

        try
        {
            if (!verificada && !string.Equals(Huellas.Calcular(s.Ruta), s.Sha, StringComparison.OrdinalIgnoreCase))
            {
                Borrar(s.Ruta);
                return Falla(id, "huella_distinta");
            }
            if (!ContenidoValido(s))
            {
                Borrar(s.Ruta);
                return Falla(id, "contenido_no_valido");
            }
            Directory.CreateDirectory(Reserva);
            string nombre = TiposPermitidos.NombreSeguro(s.Nombre);
            string destino = Path.Combine(Reserva, $"{s.Sha[..6]}-{nombre}");
            if (File.Exists(destino) && !string.Equals(Huellas.De(destino), s.Sha, StringComparison.OrdinalIgnoreCase))
                destino = Path.Combine(Reserva, $"{s.Sha[..16]}-{nombre}");
            if (File.Exists(destino)) Borrar(s.Ruta);
            else File.Move(s.Ruta, destino);
            Huellas.Recordar(destino, s.Sha);
            JsonObject r = Ok(id);
            r["url"] = Url(destino);
            return r;
        }
        catch (Exception ex)
        {
            Borrar(s.Ruta);
            return Falla(id, "error_interno", ex.Message);
        }
    }

    private static bool ContenidoValido(Subida s)
    {
        return TiposPermitidos.Archivo(s.Ruta, s.Nombre);
    }

    public JsonObject Cancelar(long id, JsonElement m)
    {
        string? subida = Protocolo.Texto(m, "subida");
        lock (_gate)
        {
            if (_actual is { } s && (subida is null || s.Id == subida)) Descartar(s);
        }
        return Ok(id);
    }

    public JsonObject Lista(long id, IReadOnlyCollection<string> usados)
    {
        var usos = usados.Select(Normalizar).GroupBy(u => u, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var lista = new List<ArchivoRemoto>();
        try
        {
            if (Directory.Exists(_assets))
                foreach (string f in Directory.EnumerateFiles(_assets, "*", SearchOption.AllDirectories).Where(TiposPermitidos.Extension).Take(MaxLista))
                {
                    var info = new FileInfo(f);
                    string url = Url(f);
                    lista.Add(new ArchivoRemoto(url, info.Name, info.Length, info.LastWriteTime, usos.GetValueOrDefault(url),
                        f.StartsWith(Reserva + "\\", StringComparison.OrdinalIgnoreCase)));
                }
        }
        catch { }
        JsonObject r = Ok(id);
        r["archivos"] = Protocolo.Nodo(lista.OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList());
        return r;
    }

    public JsonObject BorrarArchivo(long id, string? url, IReadOnlyCollection<string> usados)
    {
        if (Resolver(url) is not string full) return Falla(id, "archivo_no_encontrado");
        if (!full.StartsWith(Reserva + "\\", StringComparison.OrdinalIgnoreCase)) return Falla(id, "solo_reserva");
        if (usados.Any(u => string.Equals(Normalizar(u), Normalizar(Url(full)), StringComparison.OrdinalIgnoreCase)))
            return Falla(id, "en_uso");
        if (!File.Exists(full)) return Falla(id, "archivo_no_encontrado");
        try
        {
            File.Delete(full);
            Huellas.Olvidar(full);
        }
        catch (Exception ex) { return Falla(id, "error_interno", ex.Message); }
        return Ok(id);
    }

    public async Task<JsonObject> Miniatura(long id, JsonElement m)
    {
        if (Resolver(Protocolo.Texto(m, "url")) is not string full || !File.Exists(full)) return Falla(id, "archivo_no_encontrado");
        int ancho = Math.Clamp((int)(Protocolo.Numero(m, "ancho") ?? 256), 64, 512);
        byte[]? jpeg = await Imagenes.MiniaturaJpeg(full, ancho).ConfigureAwait(false);
        if (jpeg is null) return Falla(id, "sin_imagen");
        JsonObject r = Ok(id);
        r["jpeg"] = Convert.ToBase64String(jpeg);
        return r;
    }

    public void Pausar()
    {
        lock (_gate)
        {
            if (_actual is { } s)
            {
                Cerrar(s);
                _actual = null;
            }
        }
    }

    private static long HashearPrevio(Subida s, long previo)
    {
        try
        {
            using FileStream fs = File.OpenRead(s.Ruta);
            byte[] buf = new byte[1024 * 1024];
            long leido = 0;
            while (leido < previo)
            {
                int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, previo - leido));
                if (n <= 0) break;
                s.Hash!.AppendData(buf, 0, n);
                leido += n;
            }
            return leido;
        }
        catch
        {
            s.Hash!.GetHashAndReset();
            return 0;
        }
    }

    private static void Cerrar(Subida s)
    {
        try { s.Stream?.Dispose(); }
        catch { }
        s.Stream = null;
        s.Hash?.Dispose();
        s.Hash = null;
    }

    private void Descartar(Subida s)
    {
        Cerrar(s);
        Borrar(s.Ruta);
        if (_actual == s) _actual = null;
    }

    private void LimpiarViejos(string actual)
    {
        try
        {
            foreach (string f in Directory.EnumerateFiles(_temporal, "*.parcial"))
                if (!string.Equals(f, actual, StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7))
                    Borrar(f);
        }
        catch { }
    }

    private static void Borrar(string ruta)
    {
        try { File.Delete(ruta); }
        catch { }
    }

    public void Dispose() => Pausar();
}
