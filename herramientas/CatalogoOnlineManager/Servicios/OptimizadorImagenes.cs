using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SharpImage = SixLabors.ImageSharp.Image;

namespace CatalogoOnlineManager.Servicios;

public sealed class OptimizadorImagenes
{
    private const int MaxLongSide = 1200;
    private const int WebpQuality = 75;

    private static readonly HashSet<string> Extensiones = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp"
    };

    private static readonly HashSet<string> CarpetasOmitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "optimizer", "bin", "obj", ".git"
    };

    public async Task<int> OptimizarAsync(
        string imgDir,
        IReadOnlyList<string>? nombresCarpetas,
        bool force,
        int threads,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        imgDir = Path.GetFullPath(imgDir);
        threads = Math.Max(1, threads);

        List<string> targets;
        if (nombresCarpetas is { Count: > 0 })
        {
            targets = nombresCarpetas
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => Path.IsPathRooted(a)
                    ? Path.GetFullPath(a)
                    : Path.GetFullPath(Path.Combine(imgDir, a)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            targets = Directory.EnumerateDirectories(imgDir)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return !CarpetasOmitidas.Contains(name)
                           && Directory.Exists(Path.Combine(d, "full"));
                })
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        log("ImageOptimizer");
        log($"Base: {imgDir}");
        log($"Carpetas a procesar: {targets.Count}");
        log($"Hilos: {threads}");
        log($"Forzar regeneración: {(force ? "sí" : "no")}");
        log("");

        if (targets.Count == 0)
        {
            log("No se encontraron carpetas con 'full/' bajo img/.");
            return 1;
        }

        Configuration.Default.MaxDegreeOfParallelism = 1;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = threads,
            CancellationToken = cancellationToken
        };

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
            cancellationToken.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(targetDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var fullDir = Path.Combine(targetDir, "full");
            var optDir = Path.Combine(targetDir, "opt");

            log($"=== {folderName} ===");
            log($"Full: {fullDir}");
            log($"Opt:  {optDir}");

            if (!Directory.Exists(fullDir))
            {
                log($"  ERR No existe 'full/' en: {targetDir}");
                totalErrors++;
                log("");
                continue;
            }

            Directory.CreateDirectory(optDir);

            var groups = Directory.EnumerateFiles(fullDir)
                .Where(f => Extensiones.Contains(Path.GetExtension(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sources = groups.Select(g => g.First()).ToList();
            var duplicates = 0;

            log($"Imágenes en full: {groups.Sum(g => g.Count())}");

            foreach (var group in groups.Where(g => g.Count() > 1))
            {
                var kept = Path.GetFileName(group.First());
                foreach (var dup in group.Skip(1))
                {
                    duplicates++;
                    log($"  DUP {Path.GetFileName(dup)} (se usa {kept})");
                }
            }

            var converted = 0;
            var skipped = 0;
            var errors = 0;

            await Parallel.ForEachAsync(sources, parallelOptions, async (sourcePath, ct) =>
            {
                var name = Path.GetFileNameWithoutExtension(sourcePath);
                var destPath = Path.Combine(optDir, name + ".webp");

                var existia = File.Exists(destPath);
                if (!force && existia)
                {
                    Interlocked.Increment(ref skipped);
                    return;
                }

                try
                {
                    using var image = await SharpImage.LoadAsync(sourcePath, ct);

                    var longSide = Math.Max(image.Width, image.Height);
                    if (longSide > MaxLongSide)
                    {
                        var scale = (double)MaxLongSide / longSide;
                        var newWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
                        var newHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
                        image.Mutate(x => x.Resize(newWidth, newHeight));
                    }

                    await image.SaveAsWebpAsync(destPath, encoder, ct);
                    Interlocked.Increment(ref converted);
                    log($"  OK  {name}.webp{(existia ? " (reemplazado)" : "")}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errors);
                    Interlocked.Increment(ref totalErrors);
                    log($"  ERR {Path.GetFileName(sourcePath)}: {ex.Message}");
                }
            });

            log($"Convertidas: {converted} | Omitidas: {skipped} | Duplicados: {duplicates} | Errores: {errors}");

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
            await File.WriteAllTextAsync(folderIndexPath, JsonSerializer.Serialize(folderIndex, jsonOptions), cancellationToken);
            log($"Index: {folderIndexPath} ({folderIndex.Count} imágenes)");
            log("");
        }

        var allFolders = Directory.EnumerateDirectories(imgDir)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return !CarpetasOmitidas.Contains(name)
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

        var rootIndexPath = Path.Combine(imgDir, "index.json");
        await File.WriteAllTextAsync(rootIndexPath, JsonSerializer.Serialize(rootIndex, jsonOptions), cancellationToken);
        log($"Índice raíz: {rootIndexPath} ({rootIndex.Count} carpetas)");

        return totalErrors > 0 ? 1 : 0;
    }

    private sealed class RootIndexDocument
    {
        public string GeneratedAt { get; set; } = "";
        public int Count { get; set; }
        public List<FolderEntry> Folders { get; set; } = [];
    }

    private sealed class FolderEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string OptPath { get; set; } = "";
        public string IndexPath { get; set; } = "";
        public int ImageCount { get; set; }
    }

    private sealed class FolderIndexDocument
    {
        public string GeneratedAt { get; set; } = "";
        public string Folder { get; set; } = "";
        public int Count { get; set; }
        public List<ImageEntry> Images { get; set; } = [];
    }

    private sealed class ImageEntry
    {
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public string Path { get; set; } = "";
    }
}
