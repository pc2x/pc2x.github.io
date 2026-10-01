using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

const int MaxLongSide = 1200;
const int WebpQuality = 75;

var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    ".png", ".jpg", ".jpeg", ".webp", ".bmp"
};

// El .exe vive en /img. Sin args: procesa todas las subcarpetas con full/.
// Con args: procesa solo esas carpetas (relativas a /img o absolutas).
var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

var skipDirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "optimizer", "bin", "obj", ".git"
};

List<string> targets;
if (args.Length > 0)
{
    targets = args
        .Where(a => !string.IsNullOrWhiteSpace(a))
        .Select(a => Path.IsPathRooted(a)
            ? Path.GetFullPath(a)
            : Path.GetFullPath(Path.Combine(exeDir, a)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
else
{
    targets = Directory.EnumerateDirectories(exeDir)
        .Where(d =>
        {
            var name = Path.GetFileName(d);
            return !skipDirNames.Contains(name)
                   && Directory.Exists(Path.Combine(d, "full"));
        })
        .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
        .ToList();
}

Console.WriteLine("ImageOptimizer");
Console.WriteLine($"Base: {exeDir}");
Console.WriteLine($"Carpetas a procesar: {targets.Count}");
Console.WriteLine();

if (targets.Count == 0)
{
    Console.Error.WriteLine("No se encontraron carpetas con 'full/' bajo img/.");
    Console.Error.WriteLine("Uso: ImageOptimizer.exe              → todas las carpetas con full/");
    Console.Error.WriteLine("     ImageOptimizer.exe comidas      → solo esa carpeta");
    Console.Error.WriteLine("     ImageOptimizer.exe a b          → varias carpetas");
    return 1;
}

var encoder = new WebpEncoder
{
    Quality = WebpQuality,
    FileFormat = WebpFileFormatType.Lossy
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

var totalErrors = 0;

foreach (var targetDir in targets)
{
    var folderName = Path.GetFileName(targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    var fullDir = Path.Combine(targetDir, "full");
    var optDir = Path.Combine(targetDir, "opt");

    Console.WriteLine($"=== {folderName} ===");
    Console.WriteLine($"Full: {fullDir}");
    Console.WriteLine($"Opt:  {optDir}");

    if (!Directory.Exists(fullDir))
    {
        Console.Error.WriteLine($"  ERR No existe 'full/' en: {targetDir}");
        totalErrors++;
        Console.WriteLine();
        continue;
    }

    Directory.CreateDirectory(optDir);

    var sources = Directory.EnumerateFiles(fullDir)
        .Where(f => imageExtensions.Contains(Path.GetExtension(f)))
        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
        .ToList();

    Console.WriteLine($"Imágenes en full: {sources.Count}");

    var converted = 0;
    var skipped = 0;
    var errors = 0;

    foreach (var sourcePath in sources)
    {
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var destPath = Path.Combine(optDir, name + ".webp");

        if (File.Exists(destPath))
        {
            skipped++;
            continue;
        }

        try
        {
            using var image = Image.Load(sourcePath);

            var longSide = Math.Max(image.Width, image.Height);
            if (longSide > MaxLongSide)
            {
                var scale = (double)MaxLongSide / longSide;
                var newWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                var newHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            await image.SaveAsWebpAsync(destPath, encoder);
            converted++;
            Console.WriteLine($"  OK  {name}.webp");
        }
        catch (Exception ex)
        {
            errors++;
            totalErrors++;
            Console.Error.WriteLine($"  ERR {Path.GetFileName(sourcePath)}: {ex.Message}");
        }
    }

    Console.WriteLine($"Convertidas: {converted} | Omitidas: {skipped} | Errores: {errors}");

    var optImages = Directory.EnumerateFiles(optDir, "*.webp")
        .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
        .Select(f =>
        {
            var file = Path.GetFileName(f);
            return new ImageEntry
            {
                Name = Path.GetFileNameWithoutExtension(file),
                File = file,
                Path = $"{folderName}/opt/{file}"
            };
        })
        .ToList();

    var folderIndex = new FolderIndexDocument
    {
        GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        Folder = folderName,
        Count = optImages.Count,
        Images = optImages
    };

    var folderIndexPath = Path.Combine(optDir, "index.json");
    await File.WriteAllTextAsync(folderIndexPath, JsonSerializer.Serialize(folderIndex, jsonOptions));
    Console.WriteLine($"Index: {folderIndexPath} ({folderIndex.Count} imágenes)");
    Console.WriteLine();
}

// Índice raíz: todas las carpetas descubiertas bajo img/ (con full/), no solo las de esta corrida.
var allFolders = Directory.EnumerateDirectories(exeDir)
    .Where(d =>
    {
        var name = Path.GetFileName(d);
        return !skipDirNames.Contains(name)
               && Directory.Exists(Path.Combine(d, "full"));
    })
    .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
    .Select(d =>
    {
        var name = Path.GetFileName(d);
        var optDir = Path.Combine(d, "opt");
        var imageCount = Directory.Exists(optDir)
            ? Directory.EnumerateFiles(optDir, "*.webp").Count()
            : 0;
        return new FolderEntry
        {
            Name = name,
            Path = name,
            OptPath = $"{name}/opt",
            IndexPath = $"{name}/opt/index.json",
            ImageCount = imageCount
        };
    })
    .ToList();

var rootIndex = new RootIndexDocument
{
    GeneratedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
    Count = allFolders.Count,
    Folders = allFolders
};

var rootIndexPath = Path.Combine(exeDir, "index.json");
await File.WriteAllTextAsync(rootIndexPath, JsonSerializer.Serialize(rootIndex, jsonOptions));
Console.WriteLine($"Índice raíz: {rootIndexPath} ({rootIndex.Count} carpetas)");

return totalErrors > 0 ? 1 : 0;

internal sealed class RootIndexDocument
{
    public string GeneratedAt { get; set; } = "";
    public int Count { get; set; }
    public List<FolderEntry> Folders { get; set; } = [];
}

internal sealed class FolderEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string OptPath { get; set; } = "";
    public string IndexPath { get; set; } = "";
    public int ImageCount { get; set; }
}

internal sealed class FolderIndexDocument
{
    public string GeneratedAt { get; set; } = "";
    public string Folder { get; set; } = "";
    public int Count { get; set; }
    public List<ImageEntry> Images { get; set; } = [];
}

internal sealed class ImageEntry
{
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public string Path { get; set; } = "";
}
