using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatalogoOnlineManager.Servicios;

public sealed class GeneradorCatalogo
{
    private const int VersionesConservadas = 2;
    private const int AvisosPorArchivo = 20;
    private const string SeparadorCodigoImagen = "___";
    private const int LargoMaximoDetalle = 250;

    static GeneradorCatalogo()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <param name="rutasBase">CSV/JSON base; null o vacío = todos los de productos/fuente/.</param>
    /// <param name="rutasActualizar">CSV/JSON que sobrescriben (modo --actualizar).</param>
    public async Task<int> GenerarAsync(
        string productosDir,
        string imgDir,
        IReadOnlyList<string>? rutasBase,
        IReadOnlyList<string>? rutasActualizar,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        productosDir = Path.GetFullPath(productosDir);
        imgDir = Path.GetFullPath(imgDir);
        var fuenteDir = Path.Combine(productosDir, "fuente");
        var versionesDir = Path.Combine(productosDir, "v");

        static List<string> listar_fuentes_dir(string dir) =>
            Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir)
                    .Where(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                                || Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFullPath)
                    .ToList()
                : [];

        var modoActualizar = rutasActualizar is { Count: > 0 };
        var csvBase = modoActualizar
            ? listar_fuentes_dir(fuenteDir)
            : rutasBase is { Count: > 0 }
                ? rutasBase.Select(Path.GetFullPath).ToList()
                : listar_fuentes_dir(fuenteDir);

        var actualizar = (rutasActualizar ?? []).Select(Path.GetFullPath).ToList();

        if (modoActualizar && actualizar.Count == 0)
        {
            log("Con actualizar indica al menos un CSV/JSON de actualización.");
            return 1;
        }

        if (csvBase.Count == 0 && actualizar.Count == 0)
        {
            log($"No hay archivos .csv ni .json en {fuenteDir}.");
            return 1;
        }

        log("CatalogoProductosGenerador");
        log($"Productos: {productosDir}");
        if (modoActualizar)
        {
            log("Modo: actualizar (los CSV de actualización reemplazan nombre/precio/categoría/imagen/detalle)");
        }
        log("");

        FuenteCsv? cargar_fuente(string ruta, bool esActualizar, Action<string> logErr)
        {
            if (!File.Exists(ruta))
            {
                logErr($"No existe el CSV: {ruta}");
                return null;
            }

            var fuente = LeerFuente(ruta, logErr);
            return fuente is null ? null : fuente with { Actualizar = esActualizar };
        }

        var fuentes = new List<FuenteCsv>();
        foreach (var ruta in csvBase)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fuente = cargar_fuente(ruta, false, log);
            if (fuente is null)
            {
                return 1;
            }
            fuentes.Add(fuente);
        }

        foreach (var ruta in actualizar)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fuente = cargar_fuente(ruta, true, log);
            if (fuente is null)
            {
                return 1;
            }
            fuentes.Add(fuente);
        }

        var bases = fuentes
            .Where(f => !f.Actualizar)
            .OrderByDescending(f => (f.Columnas.ContainsKey("departamento_venta") ? 2 : 0) + (f.Columnas.ContainsKey("categoria") ? 1 : 0))
            .ThenByDescending(f => File.GetLastWriteTimeUtc(f.Ruta))
            .ToList();
        var actualizaciones = fuentes.Where(f => f.Actualizar).ToList();
        fuentes = [.. bases, .. actualizaciones];
        log("Prioridad de fuentes:");
        for (var i = 0; i < fuentes.Count; i++)
        {
            var marca = fuentes[i].Actualizar ? " [actualizar]" : "";
            log($"  {i + 1}. {Path.GetFileName(fuentes[i].Ruta)}{marca}");
        }
        log("");

        var imagenes = CargarImagenes(imgDir, log);
        log($"Imágenes en catálogo: {imagenes.PorNombre.Count} por nombre, {imagenes.PorCodigo.Count} por código");
        log("");

        var categorias = new List<string>();
        var categoriasIdx = new Dictionary<string, int>(StringComparer.Ordinal);
        var departamentos = new List<string>();
        var departamentosIdx = new Dictionary<string, int>(StringComparer.Ordinal);
        var productos = new List<Producto>();
        var porCodigo = new Dictionary<string, int>(StringComparer.Ordinal);
        var entradas = new List<(string codigo, int producto)>();

        foreach (var fuente in fuentes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var formato = fuente.Separador == '\0' ? "JSON" : $"separador '{fuente.Separador}'";
            log($"=== {Path.GetFileName(fuente.Ruta)} ({fuente.Codificacion}, {formato}, {fuente.Filas.Count - 1} filas) ===");
            var columnas = fuente.Columnas;
            var columnasCodigo = new[] { "upc", "sku", "ean" }.Where(columnas.ContainsKey).ToList();
            var columnaNombre = columnas.ContainsKey("nombre") ? "nombre" : "descripcion";
            var descartadas = 0;
            var repetidas = 0;
            var preciosCompletados = 0;
            var detallesCompletados = 0;
            var actualizados = 0;
            var nuevos = 0;
            var avisos = 0;

            void aviso(string mensaje)
            {
                if (avisos++ < AvisosPorArchivo)
                {
                    log("  " + mensaje);
                }
            }

            for (var i = 1; i < fuente.Filas.Count; i++)
            {
                var fila = AjustarColumnas(fuente.Filas[i], fuente.Encabezados.Length, columnas[columnaNombre], fuente.Separador);
                if (fila.Count == 1 && string.IsNullOrWhiteSpace(fila[0]))
                {
                    continue;
                }

                string valor(string columna) => columnas.TryGetValue(columna, out var idx) && idx < fila.Count ? fila[idx] : string.Empty;

                var codigos = columnasCodigo
                    .Select(c => (original: valor(c), normalizado: NormalizarCodigo(valor(c))))
                    .Where(c => c.normalizado.Length > 0)
                    .DistinctBy(c => c.normalizado)
                    .ToList();
                var nombre = FormatoOracion(valor(columnaNombre));
                if (codigos.Count == 0 || nombre.Length == 0 || nombre.Equals("Sin nombre en archivo", StringComparison.OrdinalIgnoreCase))
                {
                    descartadas++;
                    aviso($"DESC línea {i + 1}: sin código o sin nombre");
                    continue;
                }

                var precio = decimal.TryParse(valor("precio").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0
                    ? Math.Round(p, 2)
                    : 0m;

                int? categoria = IndiceDe(FormatoOracion(valor("categoria")), categorias, categoriasIdx);
                int? departamento = IndiceDe(EspaciosSimples(valor("departamento_venta")), departamentos, departamentosIdx);
                var detalle = DetalleDe(valor("detalle"), nombre);

                string? imagen = null;
                foreach (var codigo in codigos)
                {
                    imagen ??= ResolverImagen(valor("imagen_name"), codigo.normalizado, imagenes);
                }

                var sku = valor("sku").Trim();
                var upc = valor("upc").Trim();
                var ean = valor("ean").Trim();
                if (upc.Length == 0)
                {
                    upc = ean;
                }
                if (sku.Length == 0)
                {
                    sku = upc;
                }
                var upcPropio = upc.Length > 0 && NormalizarCodigo(upc) != NormalizarCodigo(sku) ? upc : null;

                var existente = codigos
                    .Select(c => porCodigo.TryGetValue(c.normalizado, out var idx) ? idx : -1)
                    .FirstOrDefault(idx => idx >= 0, -1);
                if (existente < 0)
                {
                    existente = productos.Count;
                    productos.Add(new Producto(sku, upcPropio, nombre, precio, categoria, departamento, imagen, detalle));
                }
                else if (fuente.Actualizar)
                {
                    var actual = productos[existente];
                    productos[existente] = actual with
                    {
                        Sku = sku.Length > 0 ? sku : actual.Sku,
                        Upc = upcPropio ?? actual.Upc,
                        Nombre = nombre,
                        Precio = precio,
                        Categoria = categoria ?? actual.Categoria,
                        Departamento = departamento ?? actual.Departamento,
                        Imagen = imagen ?? actual.Imagen,
                        Detalle = detalle ?? actual.Detalle
                    };
                    actualizados++;
                }
                else
                {
                    var actual = productos[existente];
                    if (actual.Precio == 0 && precio > 0)
                    {
                        actual = actual with { Precio = precio };
                        preciosCompletados++;
                    }
                    else if (codigos.All(c => porCodigo.ContainsKey(c.normalizado)))
                    {
                        repetidas++;
                        aviso($"REP  línea {i + 1}: código {codigos[0].normalizado} repetido (se conserva el primero)");
                    }

                    if (actual.Upc is null && upcPropio is not null && NormalizarCodigo(upcPropio) != NormalizarCodigo(actual.Sku))
                    {
                        actual = actual with { Upc = upcPropio };
                    }
                    if (actual.Detalle is null && detalle is not null)
                    {
                        actual = actual with { Detalle = detalle };
                        detallesCompletados++;
                    }
                    productos[existente] = actual with { Imagen = actual.Imagen ?? imagen };
                }

                foreach (var codigo in codigos.Where(c => !porCodigo.ContainsKey(c.normalizado)))
                {
                    porCodigo[codigo.normalizado] = existente;
                    entradas.Add((codigo.normalizado, existente));
                    nuevos++;
                }
            }

            if (avisos > AvisosPorArchivo)
            {
                log($"  ... y {avisos - AvisosPorArchivo} avisos más");
            }
            log(
                $"Nuevos: {nuevos} | Actualizados: {actualizados} | Descartados: {descartadas} | Repetidos: {repetidas} | Precios completados: {preciosCompletados} | Descripciones completadas: {detallesCompletados}");
            log("");
        }

        log($"Códigos: {entradas.Count} | Productos: {productos.Count} | Con UPC distinto al SKU: {productos.Count(p => p.Upc is not null)}");
        log($"Con imagen: {productos.Count(p => p.Imagen is not null)} | Con precio: {productos.Count(p => p.Precio > 0)} | Con descripción: {productos.Count(p => p.Detalle is not null)}");
        log($"Categorías: {categorias.Count} | Departamentos: {departamentos.Count}");

        if (productos.Count == 0)
        {
            log("No quedó ningún producto válido; no se publica versión.");
            return 1;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(versionesDir);
        var version = SiguienteVersion(versionesDir);
        var versionDir = Path.Combine(versionesDir, version);
        Directory.CreateDirectory(versionDir);
        var archivoGz = Path.Combine(versionDir, "catalogo.json.gz");

        await using (var salida = File.Create(archivoGz))
        await using (var gzip = new GZipStream(salida, CompressionLevel.SmallestSize))
        await using (var json = new Utf8JsonWriter(gzip, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();
            json.WriteString("version", version);
            json.WriteStartArray("categorias");
            categorias.ForEach(json.WriteStringValue);
            json.WriteEndArray();
            json.WriteStartArray("departamentos");
            departamentos.ForEach(json.WriteStringValue);
            json.WriteEndArray();
            json.WriteStartArray("productos");
            foreach (var (codigo, idx) in entradas)
            {
                var producto = productos[idx];
                json.WriteStartObject();
                json.WriteString("k", codigo);
                json.WriteString("n", producto.Nombre);
                if (producto.Sku != codigo)
                {
                    json.WriteString("s", producto.Sku);
                }
                if ((producto.Upc ?? producto.Sku) is var upcSalida && upcSalida != codigo)
                {
                    json.WriteString("u", upcSalida);
                }
                json.WriteNumber("p", producto.Precio);
                if (producto.Categoria is { } c)
                {
                    json.WriteNumber("c", c);
                }
                if (producto.Departamento is { } d)
                {
                    json.WriteNumber("d", d);
                }
                if (producto.Imagen is { } img)
                {
                    json.WriteString("i", img);
                }
                if (producto.Detalle is { } detalleSalida)
                {
                    json.WriteString("e", detalleSalida);
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }

        var bytesGz = new FileInfo(archivoGz).Length;
        string sha256;
        await using (var lectura = File.OpenRead(archivoGz))
        {
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(lectura, cancellationToken)).ToLowerInvariant();
        }

        var indice = new
        {
            version,
            generado = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            archivo = $"v/{version}/catalogo.json.gz",
            total = entradas.Count,
            bytes = bytesGz,
            sha256
        };

        var indicePath = Path.Combine(productosDir, "index.json");
        var indiceTemporal = indicePath + ".tmp";
        await File.WriteAllTextAsync(
            indiceTemporal,
            JsonSerializer.Serialize(indice, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        File.Move(indiceTemporal, indicePath, overwrite: true);

        log("");
        log($"Versión: {version}");
        log($"Archivo: {archivoGz} ({bytesGz:N0} bytes)");
        log($"SHA-256: {sha256}");
        log($"Índice:  {indicePath}");

        foreach (var vieja in VersionesOrdenadas(versionesDir).SkipLast(VersionesConservadas))
        {
            Directory.Delete(Path.Combine(versionesDir, vieja), recursive: true);
            log($"Borrada versión anterior: {vieja}");
        }

        return 0;
    }

    private static (string texto, string codificacion) LeerTexto(string ruta)
    {
        var bytes = File.ReadAllBytes(ruta);
        try
        {
            var texto = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return (texto.TrimStart('\uFEFF'), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(1252).GetString(bytes), "Windows-1252");
        }
    }

    private static FuenteCsv? LeerFuente(string ruta, Action<string> log)
    {
        var (texto, codificacion) = LeerTexto(ruta);
        char separador;
        List<List<string>> filas;
        if (Path.GetExtension(ruta).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            separador = '\0';
            try
            {
                filas = FilasDesdeJson(texto);
            }
            catch (JsonException e)
            {
                log($"{Path.GetFileName(ruta)}: JSON inválido ({e.Message}).");
                return null;
            }
        }
        else
        {
            var primeraLinea = texto.Split('\n', 2)[0];
            separador = primeraLinea.Count(c => c == ';') >= primeraLinea.Count(c => c == ',') ? ';' : ',';
            filas = ParsearCsv(texto, separador);
        }
        if (filas.Count == 0)
        {
            log($"El CSV está vacío: {ruta}");
            return null;
        }

        var encabezados = filas[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var columnas = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < encabezados.Length; i++)
        {
            columnas.TryAdd(encabezados[i], i);
        }

        if (!columnas.ContainsKey("sku") && !columnas.ContainsKey("upc") && !columnas.ContainsKey("ean"))
        {
            log($"{Path.GetFileName(ruta)}: falta la columna 'sku', 'upc' o 'ean'.");
            return null;
        }
        if (!columnas.ContainsKey("nombre") && !columnas.ContainsKey("descripcion"))
        {
            log($"{Path.GetFileName(ruta)}: falta la columna 'nombre' o 'descripcion'.");
            return null;
        }

        return new FuenteCsv(ruta, codificacion, separador, encabezados, columnas, filas);
    }

    private static List<List<string>> FilasDesdeJson(string texto)
    {
        using var documento = JsonDocument.Parse(texto);
        var objetos = documento.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList();
        var llaves = objetos
            .SelectMany(o => o.EnumerateObject().Select(p => p.Name.Trim().ToLowerInvariant()))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var filas = new List<List<string>> { llaves };
        foreach (var objeto in objetos)
        {
            var fila = Enumerable.Repeat(string.Empty, llaves.Count).ToList();
            foreach (var propiedad in objeto.EnumerateObject())
            {
                fila[llaves.IndexOf(propiedad.Name.Trim().ToLowerInvariant())] = propiedad.Value.ValueKind switch
                {
                    JsonValueKind.String => propiedad.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => propiedad.Value.GetDecimal().ToString(CultureInfo.InvariantCulture),
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    _ => propiedad.Value.GetRawText()
                };
            }
            filas.Add(fila);
        }

        return filas;
    }

    private static List<string> AjustarColumnas(List<string> fila, int esperadas, int columnaNombre, char separador)
    {
        var sobrantes = fila.Count - esperadas;
        if (sobrantes <= 0)
        {
            return fila;
        }

        var ajustada = new List<string>(esperadas);
        ajustada.AddRange(fila.Take(columnaNombre));
        ajustada.Add(string.Join(separador, fila.Skip(columnaNombre).Take(sobrantes + 1)));
        ajustada.AddRange(fila.Skip(columnaNombre + sobrantes + 1));
        return ajustada;
    }

    private static int? IndiceDe(string texto, List<string> lista, Dictionary<string, int> indices)
    {
        if (texto.Length == 0)
        {
            return null;
        }
        if (!indices.TryGetValue(texto, out var idx))
        {
            idx = lista.Count;
            lista.Add(texto);
            indices[texto] = idx;
        }
        return idx;
    }

    private static List<List<string>> ParsearCsv(string texto, char separador)
    {
        var filas = new List<List<string>>();
        var fila = new List<string>();
        var campo = new StringBuilder();
        var entreComillas = false;

        for (var i = 0; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (entreComillas)
            {
                if (ch == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"')
                    {
                        campo.Append('"');
                        i++;
                    }
                    else
                    {
                        entreComillas = false;
                    }
                }
                else
                {
                    campo.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"' when campo.Length == 0:
                    entreComillas = true;
                    break;
                case var _ when ch == separador:
                    fila.Add(campo.ToString());
                    campo.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fila.Add(campo.ToString());
                    campo.Clear();
                    filas.Add(fila);
                    fila = [];
                    break;
                default:
                    campo.Append(ch);
                    break;
            }
        }

        if (campo.Length > 0 || fila.Count > 0)
        {
            fila.Add(campo.ToString());
            filas.Add(fila);
        }

        return filas;
    }

    private static string NormalizarCodigo(string? codigo)
    {
        var limpio = Regex.Replace(codigo ?? string.Empty, @"\s+", string.Empty).ToLowerInvariant();
        if (limpio.Length == 0)
        {
            return string.Empty;
        }
        var sinCeros = limpio.TrimStart('0');
        return sinCeros.Length == 0 ? "0" : sinCeros;
    }

    private static string EspaciosSimples(string? texto) =>
        Regex.Replace((texto ?? string.Empty).Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();

    private static string? DetalleDe(string? texto, string nombre)
    {
        var limpio = EspaciosSimples(texto);
        if (limpio.Length > LargoMaximoDetalle)
        {
            limpio = limpio[..LargoMaximoDetalle].TrimEnd();
        }
        return limpio.Length == 0 || limpio.Equals(nombre, StringComparison.OrdinalIgnoreCase) ? null : limpio;
    }

    private static string FormatoOracion(string? texto)
    {
        var limpio = EspaciosSimples(texto).ToLower(CultureInfo.GetCultureInfo("es-MX"));
        for (var i = 0; i < limpio.Length; i++)
        {
            if (char.IsDigit(limpio[i]))
            {
                break;
            }
            if (char.IsLetter(limpio[i]))
            {
                return string.Concat(
                    limpio.AsSpan(0, i),
                    char.ToUpper(limpio[i], CultureInfo.GetCultureInfo("es-MX")).ToString(),
                    limpio.AsSpan(i + 1));
            }
        }
        return limpio;
    }

    private static string LlaveImagen(string nombre) =>
        nombre.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();

    private static Imagenes CargarImagenes(string imgDir, Action<string> log)
    {
        var porNombre = new Dictionary<string, string>(StringComparer.Ordinal);
        var porCodigo = new Dictionary<string, string>(StringComparer.Ordinal);
        var raiz = Path.Combine(imgDir, "index.json");
        if (!File.Exists(raiz))
        {
            log($"  AVISO no existe {raiz}; el catálogo sale sin imágenes.");
            return new Imagenes(porNombre, porCodigo);
        }

        var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var indiceRaiz = JsonSerializer.Deserialize<IndiceRaiz>(File.ReadAllText(raiz), opciones);
        foreach (var carpeta in indiceRaiz?.Folders ?? [])
        {
            var indiceCarpeta = Path.Combine(imgDir, carpeta.IndexPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(indiceCarpeta))
            {
                log($"  AVISO no existe {indiceCarpeta}");
                continue;
            }

            var contenido = JsonSerializer.Deserialize<IndiceCarpeta>(File.ReadAllText(indiceCarpeta), opciones);
            foreach (var imagen in contenido?.Images ?? [])
            {
                var ruta = $"{carpeta.OptPath}/{imagen.File}";
                porNombre.TryAdd(LlaveImagen(imagen.Name), ruta);

                var separador = imagen.Name.IndexOf(SeparadorCodigoImagen, StringComparison.Ordinal);
                if (separador > 0)
                {
                    var prefijo = imagen.Name[..separador];
                    var punto = prefijo.IndexOf('.');
                    if (punto > 0)
                    {
                        prefijo = prefijo[..punto];
                    }
                    var codigo = NormalizarCodigo(prefijo);
                    if (codigo.Length > 0)
                    {
                        porCodigo.TryAdd(codigo, ruta);
                    }
                }
            }
        }

        return new Imagenes(porNombre, porCodigo);
    }

    private static string? ResolverImagen(string imagenName, string codigo, Imagenes imagenes)
    {
        var nombre = imagenName.Trim();
        if (nombre.Length > 0)
        {
            var sinExtension = Path.GetFileNameWithoutExtension(nombre);
            if (imagenes.PorNombre.TryGetValue(LlaveImagen(sinExtension), out var ruta)
                || imagenes.PorNombre.TryGetValue(LlaveImagen(nombre), out ruta))
            {
                return ruta;
            }
        }

        return imagenes.PorCodigo.TryGetValue(codigo, out var porCodigo) ? porCodigo : null;
    }

    private static IEnumerable<string> VersionesOrdenadas(string versionesDir) =>
        Directory.EnumerateDirectories(versionesDir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(nombre => (nombre, partes: ParsearVersion(nombre)))
            .Where(v => v.partes is not null)
            .OrderBy(v => v.partes!.Value.fecha)
            .ThenBy(v => v.partes!.Value.consecutivo)
            .Select(v => v.nombre);

    private static (DateOnly fecha, int consecutivo)? ParsearVersion(string nombre)
    {
        var punto = nombre.LastIndexOf('.');
        if (punto <= 0
            || !DateOnly.TryParseExact(nombre[..punto], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            || !int.TryParse(nombre[(punto + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var consecutivo))
        {
            return null;
        }
        return (fecha, consecutivo);
    }

    private static string SiguienteVersion(string versionesDir)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        var consecutivo = VersionesOrdenadas(versionesDir)
            .Select(ParsearVersion)
            .Where(v => v!.Value.fecha == hoy)
            .Select(v => v!.Value.consecutivo)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{hoy:yyyy-MM-dd}.{consecutivo}";
    }

    private sealed record FuenteCsv(
        string Ruta,
        string Codificacion,
        char Separador,
        string[] Encabezados,
        Dictionary<string, int> Columnas,
        List<List<string>> Filas,
        bool Actualizar = false);

    private sealed record Producto(
        string Sku,
        string? Upc,
        string Nombre,
        decimal Precio,
        int? Categoria,
        int? Departamento,
        string? Imagen,
        string? Detalle);

    private sealed record Imagenes(
        Dictionary<string, string> PorNombre,
        Dictionary<string, string> PorCodigo);

    private sealed class IndiceRaiz
    {
        public List<CarpetaImagenes> Folders { get; set; } = [];
    }

    private sealed class CarpetaImagenes
    {
        public string Name { get; set; } = "";
        public string OptPath { get; set; } = "";
        public string IndexPath { get; set; } = "";
    }

    private sealed class IndiceCarpeta
    {
        public List<ImagenCatalogo> Images { get; set; } = [];
    }

    private sealed class ImagenCatalogo
    {
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
    }
}
