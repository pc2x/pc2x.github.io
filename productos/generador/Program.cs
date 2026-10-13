using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

const int VersionesConservadas = 2;
const int AvisosPorArchivo = 20;
const string SeparadorCodigoImagen = "___";

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// El .exe vive en /productos. Al ejecutarse desde bin/ se sube hasta encontrar la raíz del sitio (img/ + productos/).
var productosDir = resolver_dir_productos(AppContext.BaseDirectory);
if (productosDir is null)
{
    Console.Error.WriteLine("No se encontró la carpeta productos/ (junto a img/) a partir de: " + AppContext.BaseDirectory);
    return 1;
}

var imgDir = Path.Combine(Path.GetDirectoryName(productosDir)!, "img");
var fuenteDir = Path.Combine(productosDir, "fuente");
var versionesDir = Path.Combine(productosDir, "v");

var csvPaths = args.Length > 0
    ? args.Select(Path.GetFullPath).ToList()
    : Directory.Exists(fuenteDir)
        ? Directory.EnumerateFiles(fuenteDir)
            .Where(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(f).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .ToList()
        : [];
if (csvPaths.Count == 0)
{
    Console.Error.WriteLine($"No hay archivos .csv ni .json en {fuenteDir}.");
    Console.Error.WriteLine("Uso: CatalogoProductosGenerador.exe [a.csv b.json ...]   (sin argumentos: todos los .csv y .json de productos/fuente)");
    return 1;
}

Console.WriteLine("CatalogoProductosGenerador");
Console.WriteLine($"Productos: {productosDir}");
Console.WriteLine();

var fuentes = new List<FuenteCsv>();
foreach (var ruta in csvPaths)
{
    if (!File.Exists(ruta))
    {
        Console.Error.WriteLine($"No existe el CSV: {ruta}");
        return 1;
    }

    var fuente = leer_fuente(ruta);
    if (fuente is null)
    {
        return 1;
    }
    fuentes.Add(fuente);
}

// Gana el primer archivo que trae un código (nombre, categoría e imagen); los siguientes solo agregan códigos
// faltantes y completan precios en 0. Orden: con departamento, con categoría, sin categoría; a igualdad, el más reciente.
fuentes = fuentes
    .OrderByDescending(f => (f.Columnas.ContainsKey("departamento_venta") ? 2 : 0) + (f.Columnas.ContainsKey("categoria") ? 1 : 0))
    .ThenByDescending(f => File.GetLastWriteTimeUtc(f.Ruta))
    .ToList();
Console.WriteLine("Prioridad de fuentes:");
for (var i = 0; i < fuentes.Count; i++)
{
    Console.WriteLine($"  {i + 1}. {Path.GetFileName(fuentes[i].Ruta)}");
}
Console.WriteLine();

var imagenes = cargar_imagenes(imgDir);
Console.WriteLine($"Imágenes en catálogo: {imagenes.porNombre.Count} por nombre, {imagenes.porCodigo.Count} por código");
Console.WriteLine();

var categorias = new List<string>();
var categoriasIdx = new Dictionary<string, int>(StringComparer.Ordinal);
var departamentos = new List<string>();
var departamentosIdx = new Dictionary<string, int>(StringComparer.Ordinal);
var productos = new List<Producto>();
var porCodigo = new Dictionary<string, int>(StringComparer.Ordinal);

foreach (var fuente in fuentes)
{
    var formato = fuente.Separador == '\0' ? "JSON" : $"separador '{fuente.Separador}'";
    Console.WriteLine($"=== {Path.GetFileName(fuente.Ruta)} ({fuente.Codificacion}, {formato}, {fuente.Filas.Count - 1} filas) ===");
    var columnas = fuente.Columnas;
    var columnasCodigo = new[] { "upc", "sku" }.Where(columnas.ContainsKey).ToList();
    // En el CSV de la tienda el nombre va en "nombre"; en el de retail viene en "descripcion".
    var columnaNombre = columnas.ContainsKey("nombre") ? "nombre" : "descripcion";
    var descartadas = 0;
    var repetidas = 0;
    var preciosCompletados = 0;
    var nuevos = 0;
    var avisos = 0;

    void aviso(string mensaje)
    {
        if (avisos++ < AvisosPorArchivo)
        {
            Console.WriteLine("  " + mensaje);
        }
    }

    for (var i = 1; i < fuente.Filas.Count; i++)
    {
        var fila = ajustar_columnas(fuente.Filas[i], fuente.Encabezados.Length, columnas[columnaNombre], fuente.Separador);
        if (fila.Count == 1 && string.IsNullOrWhiteSpace(fila[0]))
        {
            continue;
        }

        string valor(string columna) => columnas.TryGetValue(columna, out var idx) && idx < fila.Count ? fila[idx] : string.Empty;

        var codigos = columnasCodigo
            .Select(c => (original: valor(c), normalizado: normalizar_codigo(valor(c))))
            .Where(c => c.normalizado.Length > 0)
            .DistinctBy(c => c.normalizado)
            .ToList();
        var nombre = formato_oracion(valor(columnaNombre));
        if (codigos.Count == 0 || nombre.Length == 0 || nombre.Equals("Sin nombre en archivo", StringComparison.OrdinalIgnoreCase))
        {
            descartadas++;
            aviso($"DESC línea {i + 1}: sin código o sin nombre");
            continue;
        }

        var precio = decimal.TryParse(valor("precio").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0
            ? Math.Round(p, 2)
            : 0m;

        int? categoria = indice_de(formato_oracion(valor("categoria")), categorias, categoriasIdx);
        int? departamento = indice_de(espacios_simples(valor("departamento_venta")), departamentos, departamentosIdx);

        string? imagen = null;
        foreach (var codigo in codigos)
        {
            imagen ??= resolver_imagen(valor("imagen_name"), codigo.normalizado, imagenes);
        }

        // UPC y SKU son llaves del mismo producto: el que escanee cualquiera de los dos lo encuentra.
        foreach (var codigo in codigos)
        {
            if (porCodigo.TryGetValue(codigo.normalizado, out var existente))
            {
                if (productos[existente].Precio == 0 && precio > 0)
                {
                    productos[existente] = productos[existente] with { Precio = precio };
                    preciosCompletados++;
                }
                else
                {
                    repetidas++;
                    aviso($"REP  línea {i + 1}: código {codigo.normalizado} repetido (se conserva el primero)");
                }
                continue;
            }

            porCodigo[codigo.normalizado] = productos.Count;
            productos.Add(new Producto(codigo.normalizado, nombre, precio, categoria, departamento, imagen));
            nuevos++;
        }
    }

    if (avisos > AvisosPorArchivo)
    {
        Console.WriteLine($"  ... y {avisos - AvisosPorArchivo} avisos más");
    }
    Console.WriteLine($"Nuevos: {nuevos} | Descartados: {descartadas} | Repetidos: {repetidas} | Precios completados: {preciosCompletados}");
    Console.WriteLine();
}

Console.WriteLine($"Productos: {productos.Count} | Con imagen: {productos.Count(p => p.Imagen is not null)} | Con precio: {productos.Count(p => p.Precio > 0)}");
Console.WriteLine($"Categorías: {categorias.Count} | Departamentos: {departamentos.Count}");

if (productos.Count == 0)
{
    Console.Error.WriteLine("No quedó ningún producto válido; no se publica versión.");
    return 1;
}

Directory.CreateDirectory(versionesDir);
var version = siguiente_version(versionesDir);
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
    foreach (var producto in productos)
    {
        json.WriteStartObject();
        json.WriteString("k", producto.Codigo);
        json.WriteString("n", producto.Nombre);
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
        json.WriteEndObject();
    }
    json.WriteEndArray();
    json.WriteEndObject();
}

var bytesGz = new FileInfo(archivoGz).Length;
string sha256;
await using (var lectura = File.OpenRead(archivoGz))
{
    sha256 = Convert.ToHexString(await SHA256.HashDataAsync(lectura)).ToLowerInvariant();
}

var indice = new
{
    version,
    generado = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
    archivo = $"v/{version}/catalogo.json.gz",
    total = productos.Count,
    bytes = bytesGz,
    sha256
};

// index.json se reemplaza al final para que nunca apunte a un archivo incompleto.
var indicePath = Path.Combine(productosDir, "index.json");
var indiceTemporal = indicePath + ".tmp";
await File.WriteAllTextAsync(indiceTemporal, JsonSerializer.Serialize(indice, new JsonSerializerOptions { WriteIndented = true }));
File.Move(indiceTemporal, indicePath, overwrite: true);

Console.WriteLine();
Console.WriteLine($"Versión: {version}");
Console.WriteLine($"Archivo: {archivoGz} ({bytesGz:N0} bytes)");
Console.WriteLine($"SHA-256: {sha256}");
Console.WriteLine($"Índice:  {indicePath}");

foreach (var vieja in versiones_ordenadas(versionesDir).SkipLast(VersionesConservadas))
{
    Directory.Delete(Path.Combine(versionesDir, vieja), recursive: true);
    Console.WriteLine($"Borrada versión anterior: {vieja}");
}

return 0;

static string? resolver_dir_productos(string inicio)
{
    var dir = new DirectoryInfo(Path.TrimEndingDirectorySeparator(inicio));
    while (dir is not null)
    {
        if (dir.Name.Equals("productos", StringComparison.OrdinalIgnoreCase)
            && dir.Parent is not null
            && Directory.Exists(Path.Combine(dir.Parent.FullName, "img")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return null;
}

static (string texto, string codificacion) leer_texto(string ruta)
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

static FuenteCsv? leer_fuente(string ruta)
{
    var (texto, codificacion) = leer_texto(ruta);
    char separador;
    List<List<string>> filas;
    if (Path.GetExtension(ruta).Equals(".json", StringComparison.OrdinalIgnoreCase))
    {
        separador = '\0';
        try
        {
            filas = filas_desde_json(texto);
        }
        catch (JsonException e)
        {
            Console.Error.WriteLine($"{Path.GetFileName(ruta)}: JSON inválido ({e.Message}).");
            return null;
        }
    }
    else
    {
        var primeraLinea = texto.Split('\n', 2)[0];
        // El CSV de la tienda usa ';' y el de retail ','; se elige el que más aparece en el encabezado.
        separador = primeraLinea.Count(c => c == ';') >= primeraLinea.Count(c => c == ',') ? ';' : ',';
        filas = parsear_csv(texto, separador);
    }
    if (filas.Count == 0)
    {
        Console.Error.WriteLine($"El CSV está vacío: {ruta}");
        return null;
    }

    var encabezados = filas[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
    var columnas = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var i = 0; i < encabezados.Length; i++)
    {
        columnas.TryAdd(encabezados[i], i);
    }

    if (!columnas.ContainsKey("sku") && !columnas.ContainsKey("upc"))
    {
        Console.Error.WriteLine($"{Path.GetFileName(ruta)}: falta la columna 'sku' o 'upc'.");
        return null;
    }
    if (!columnas.ContainsKey("nombre") && !columnas.ContainsKey("descripcion"))
    {
        Console.Error.WriteLine($"{Path.GetFileName(ruta)}: falta la columna 'nombre' o 'descripcion'.");
        return null;
    }

    return new FuenteCsv(ruta, codificacion, separador, encabezados, columnas, filas);
}

// Arreglo de objetos planos ([{ "sku": "...", "nombre": "...", "precio": 3.0, ... }]) convertido a filas,
// con las llaves como encabezado; así pasa por las mismas reglas que un CSV.
static List<List<string>> filas_desde_json(string texto)
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

// Filas con el separador sin comillas dentro del nombre traen columnas de más: se reintegran al nombre.
static List<string> ajustar_columnas(List<string> fila, int esperadas, int columnaNombre, char separador)
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

static int? indice_de(string texto, List<string> lista, Dictionary<string, int> indices)
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

static List<List<string>> parsear_csv(string texto, char separador)
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

// Misma regla que CatalogoImagenesIndice en LethorPos: minúsculas, sin espacios y sin ceros a la izquierda.
static string normalizar_codigo(string? codigo)
{
    var limpio = Regex.Replace(codigo ?? string.Empty, @"\s+", string.Empty).ToLowerInvariant();
    if (limpio.Length == 0)
    {
        return string.Empty;
    }
    var sinCeros = limpio.TrimStart('0');
    return sinCeros.Length == 0 ? "0" : sinCeros;
}

static string espacios_simples(string? texto)
{
    return Regex.Replace((texto ?? string.Empty).Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
}

static string formato_oracion(string? texto)
{
    var limpio = espacios_simples(texto).ToLower(CultureInfo.GetCultureInfo("es-MX"));
    // "+kool ..." → "+Kool ...", pero "3pack ..." se queda igual: solo se capitaliza si no hay un dígito antes.
    for (var i = 0; i < limpio.Length; i++)
    {
        if (char.IsDigit(limpio[i]))
        {
            break;
        }
        if (char.IsLetter(limpio[i]))
        {
            return string.Concat(limpio.AsSpan(0, i), char.ToUpper(limpio[i], CultureInfo.GetCultureInfo("es-MX")).ToString(), limpio.AsSpan(i + 1));
        }
    }
    return limpio;
}

static string llave_imagen(string nombre)
{
    return nombre.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
}

static Imagenes cargar_imagenes(string imgDir)
{
    var porNombre = new Dictionary<string, string>(StringComparer.Ordinal);
    var porCodigo = new Dictionary<string, string>(StringComparer.Ordinal);
    var raiz = Path.Combine(imgDir, "index.json");
    if (!File.Exists(raiz))
    {
        Console.WriteLine($"  AVISO no existe {raiz}; el catálogo sale sin imágenes.");
        return new Imagenes(porNombre, porCodigo);
    }

    var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var indiceRaiz = JsonSerializer.Deserialize<IndiceRaiz>(File.ReadAllText(raiz), opciones);
    foreach (var carpeta in indiceRaiz?.Folders ?? [])
    {
        var indiceCarpeta = Path.Combine(imgDir, carpeta.IndexPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(indiceCarpeta))
        {
            Console.WriteLine($"  AVISO no existe {indiceCarpeta}");
            continue;
        }

        var contenido = JsonSerializer.Deserialize<IndiceCarpeta>(File.ReadAllText(indiceCarpeta), opciones);
        foreach (var imagen in contenido?.Images ?? [])
        {
            var ruta = $"{carpeta.OptPath}/{imagen.File}";
            porNombre.TryAdd(llave_imagen(imagen.Name), ruta);

            var separador = imagen.Name.IndexOf(SeparadorCodigoImagen, StringComparison.Ordinal);
            if (separador > 0)
            {
                var prefijo = imagen.Name[..separador];
                var punto = prefijo.IndexOf('.');
                if (punto > 0)
                {
                    prefijo = prefijo[..punto];
                }
                var codigo = normalizar_codigo(prefijo);
                if (codigo.Length > 0)
                {
                    porCodigo.TryAdd(codigo, ruta);
                }
            }
        }
    }

    return new Imagenes(porNombre, porCodigo);
}

static string? resolver_imagen(string imagenName, string codigo, Imagenes imagenes)
{
    var nombre = imagenName.Trim();
    if (nombre.Length > 0)
    {
        var sinExtension = Path.GetFileNameWithoutExtension(nombre);
        if (imagenes.porNombre.TryGetValue(llave_imagen(sinExtension), out var ruta)
            || imagenes.porNombre.TryGetValue(llave_imagen(nombre), out ruta))
        {
            return ruta;
        }
    }

    return imagenes.porCodigo.TryGetValue(codigo, out var porCodigo) ? porCodigo : null;
}

static IEnumerable<string> versiones_ordenadas(string versionesDir)
{
    return Directory.EnumerateDirectories(versionesDir)
        .Select(Path.GetFileName)
        .OfType<string>()
        .Select(nombre => (nombre, partes: parsear_version(nombre)))
        .Where(v => v.partes is not null)
        .OrderBy(v => v.partes!.Value.fecha)
        .ThenBy(v => v.partes!.Value.consecutivo)
        .Select(v => v.nombre);
}

static (DateOnly fecha, int consecutivo)? parsear_version(string nombre)
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

static string siguiente_version(string versionesDir)
{
    var hoy = DateOnly.FromDateTime(DateTime.Now);
    var consecutivo = versiones_ordenadas(versionesDir)
        .Select(parsear_version)
        .Where(v => v!.Value.fecha == hoy)
        .Select(v => v!.Value.consecutivo)
        .DefaultIfEmpty(0)
        .Max() + 1;
    return $"{hoy:yyyy-MM-dd}.{consecutivo}";
}

internal sealed record FuenteCsv(
    string Ruta,
    string Codificacion,
    char Separador,
    string[] Encabezados,
    Dictionary<string, int> Columnas,
    List<List<string>> Filas);

internal sealed record Producto(string Codigo, string Nombre, decimal Precio, int? Categoria, int? Departamento, string? Imagen);

internal sealed record Imagenes(Dictionary<string, string> porNombre, Dictionary<string, string> porCodigo);

internal sealed class IndiceRaiz
{
    public List<CarpetaImagenes> Folders { get; set; } = [];
}

internal sealed class CarpetaImagenes
{
    public string Name { get; set; } = "";
    public string OptPath { get; set; } = "";
    public string IndexPath { get; set; } = "";
}

internal sealed class IndiceCarpeta
{
    public List<ImagenCatalogo> Images { get; set; } = [];
}

internal sealed class ImagenCatalogo
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
}
