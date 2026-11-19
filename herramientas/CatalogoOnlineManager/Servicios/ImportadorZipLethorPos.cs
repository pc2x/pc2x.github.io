using System.IO.Compression;

namespace CatalogoOnlineManager.Servicios;

public sealed class ImportadorZipLethorPos
{
    private readonly OptimizadorImagenes _optimizador = new();
    private readonly GeneradorCatalogo _generador = new();

    public async Task<int> ImportarAsync(
        RepoSitio repo,
        string rutaZip,
        string nombreCarpetaImg,
        int threads,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(rutaZip))
        {
            log($"ERROR: no existe el ZIP: {rutaZip}");
            return 1;
        }

        nombreCarpetaImg = string.IsNullOrWhiteSpace(nombreCarpetaImg) ? "General" : nombreCarpetaImg.Trim();
        var stage = Path.Combine(Path.GetTempPath(), "lethorpos_catalogo_zip_" + Guid.NewGuid().ToString("N"));
        var unzip = Path.Combine(stage, "unzip");

        try
        {
            Directory.CreateDirectory(unzip);
            log($"Descomprimiendo ZIP: {rutaZip}");
            ZipFile.ExtractToDirectory(rutaZip, unzip, overwriteFiles: true);

            var csv = Directory.EnumerateFiles(unzip, "*.csv", SearchOption.AllDirectories).FirstOrDefault();
            if (csv is null)
            {
                log("ERROR: el ZIP no contiene un CSV.");
                return 1;
            }

            log($"CSV: {csv}");

            var imgSrc = Directory.EnumerateDirectories(unzip, "imagenes", SearchOption.AllDirectories).FirstOrDefault();
            var fullDir = repo.RutaFull(nombreCarpetaImg);
            Directory.CreateDirectory(fullDir);

            var copiadas = 0;
            if (imgSrc is not null)
            {
                log($"Copiando imágenes a img\\{nombreCarpetaImg}\\full\\ ...");
                foreach (var archivo in Directory.EnumerateFiles(imgSrc))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destino = Path.Combine(fullDir, Path.GetFileName(archivo));
                    File.Copy(archivo, destino, overwrite: true);
                    copiadas++;
                }
                log($"Imágenes copiadas: {copiadas}");
            }
            else
            {
                log("Aviso: el ZIP no trae carpeta imagenes/; se actualizan solo datos del CSV.");
            }

            log("");
            log($"Optimizando imágenes (force, carpeta {nombreCarpetaImg})...");
            var codigoOpt = await _optimizador.OptimizarAsync(
                repo.ImgDir,
                [nombreCarpetaImg],
                force: true,
                threads,
                log,
                cancellationToken);
            if (codigoOpt != 0)
            {
                log("ERROR: la optimización de imágenes falló.");
                return codigoOpt;
            }

            log("");
            log("Generando catálogo (actualizar con CSV del ZIP)...");
            var codigoGen = await _generador.GenerarAsync(
                repo.ProductosDir,
                repo.ImgDir,
                rutasBase: null,
                rutasActualizar: [csv],
                log,
                cancellationToken);
            if (codigoGen != 0)
            {
                log("ERROR: la generación del catálogo falló.");
                return codigoGen;
            }

            log("");
            log("Listo. Revisa el diff de productos\\ e img\\ y haz commit/push a main.");
            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(stage))
                {
                    Directory.Delete(stage, recursive: true);
                }
            }
            catch (Exception ex)
            {
                log($"Aviso: no se pudo borrar temp {stage}: {ex.Message}");
            }
        }
    }
}
