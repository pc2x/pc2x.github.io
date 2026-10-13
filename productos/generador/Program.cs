using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

const int VersionesConservadas = 2;
const string SeparadorCodigoImagen = "___";
string[] columnasEsperadas = ["sku", "nombre", "descripcion", "precio", "categoria", "imagen_name", "pagina", "departamento_venta"];

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

string csvPath;
if (args.Length > 0)
{
    csvPath = Path.GetFullPath(args[0]);
}
else
{
    var csvs = Directory.Exists(fuenteDir) ? Directory.GetFiles(fuenteDir, "*.csv") : [];
    if (csvs.Length != 1)
    {
        Console.Error.WriteLine($"Se esperaba exactamente un .csv en {fuenteDir} (hay {csvs.Length}).");
        Console.Error.WriteLine("Uso: CatalogoProductosGenerador.exe [ruta.csv]");
        return 1;
    }
    csvPath = csvs[0];
}

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"No existe el CSV: {csvPath}");
    return 1;
}

Console.WriteLine("CatalogoProductosGenerador");
Console.WriteLine($"Productos: {productosDir}");
Console.WriteLine($"CSV:       {csvPath}");
Console.WriteLine();

var (texto, codificacion) = leer_texto(csvPath);
Console.WriteLine($"Codificación detectada: {codificacion}");

var filas = parsear_csv(texto);
if (filas.Count == 0)
{
    Console.Error.WriteLine("El CSV está vacío.");
    return 1;
}

var encabezados = filas[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
var indices = new Dictionary<string, int>();
foreach (var columna in columnasEsperadas)
{
    var idx = Array.IndexOf(encabezados, columna);
    if (idx < 0)
    {
        Console.Error.WriteLine($"Falta la columna '{columna}' en el encabezado.");
        return 1;
    }
    indices[columna] = idx;
}

var imagenes = cargar_imagenes(imgDir);
Console.WriteLine($"Imágenes en catálogo: {imagenes.porNombre.Count} por nombre, {imagenes.porCodigo.Count} por código");

var categorias = new List<string>();
var categoriasIdx = new Dictionary<string, int>(StringComparer.Ordinal);
var departamentos = new List<string>();
var departamentosIdx = new Dictionary<string, int>(StringComparer.Ordinal);
var productos = new List<Producto>();
var vistos = new HashSet<string>(StringComparer.Ordinal);
var descartadas = 0;
var repetidas = 0;
var conImagen = 0;
var conPrecio = 0;

for (var i = 1; i < filas.Count; i++)
{
    var fila = filas[i];
    if (fila.Count == 1 && string.IsNullOrWhiteSpace(fila[0]))
    {
        continue;
    }

    string valor(string columna) => indices[columna] < fila.Count ? fila[indices[columna]] : string.Empty;

    var codigo = normalizar_codigo(valor("sku"));
    var nombre = formato_oracion(valor("nombre"));
    if (codigo.Length == 0 || nombre.Length == 0)
    {
        descartadas++;
        Console.WriteLine($"  DESC línea {i + 1}: sin código o sin nombre");
        continue;
    }

    if (!vistos.Add(codigo))
    {
        repetidas++;
        Console.WriteLine($"  REP  línea {i + 1}: código {codigo} repetido (se conserva el primero)");
        continue;
    }

    var precio = decimal.TryParse(valor("precio").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var p) && p > 0
        ? Math.Round(p, 2)
        : 0m;
    if (precio > 0)
    {
        conPrecio++;
    }

    int? categoria = null;
    var textoCategoria = formato_oracion(valor("categoria"));
    if (textoCategoria.Length > 0)
    {
        if (!categoriasIdx.TryGetValue(textoCategoria, out var c))
        {
            c = categorias.Count;
            categorias.Add(textoCategoria);
            categoriasIdx[textoCategoria] = c;
        }
        categoria = c;
    }

    int? departamento = null;
    var textoDepartamento = espacios_simples(valor("departamento_venta"));
    if (textoDepartamento.Length > 0)
    {
        if (!departamentosIdx.TryGetValue(textoDepartamento, out var d))
        {
            d = departamentos.Count;
            departamentos.Add(textoDepartamento);
            departamentosIdx[textoDepartamento] = d;
        }
        departamento = d;
    }

    var imagen = resolver_imagen(valor("imagen_name"), codigo, imagenes);
    if (imagen is not null)
    {
        conImagen++;
    }

    productos.Add(new Producto(codigo, nombre, precio, categoria, departamento, imagen));
}

Console.WriteLine();
Console.WriteLine($"Productos: {productos.Count} | Descartados: {descartadas} | Repetidos: {repetidas} | Con imagen: {conImagen} | Con precio: {conPrecio}");
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

static List<List<string>> parsear_csv(string texto)
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
            case ';':
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
