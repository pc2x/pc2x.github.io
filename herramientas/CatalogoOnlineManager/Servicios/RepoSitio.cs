namespace CatalogoOnlineManager.Servicios;

/// <summary>Resuelve la raíz del sitio estático (carpetas img/ + productos/).</summary>
public sealed class RepoSitio
{
    private static readonly HashSet<string> CarpetasOmitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "optimizer", "bin", "obj", ".git"
    };

    public string Raiz { get; private set; }

    public string ImgDir => Path.Combine(Raiz, "img");
    public string ProductosDir => Path.Combine(Raiz, "productos");
    public string FuenteDir => Path.Combine(ProductosDir, "fuente");
    public string VersionesDir => Path.Combine(ProductosDir, "v");

    public RepoSitio(string? raiz = null)
    {
        Raiz = raiz is not null && EsRaizValida(raiz)
            ? Path.GetFullPath(raiz)
            : EncontrarDesde(AppContext.BaseDirectory)
              ?? throw new InvalidOperationException(
                  "No se encontró la raíz del sitio (carpetas img/ y productos/). Usa «Cambiar carpeta…».");
    }

    public void CambiarRaiz(string ruta)
    {
        if (!EsRaizValida(ruta))
        {
            throw new InvalidOperationException("La carpeta debe contener img/ y productos/.");
        }

        Raiz = Path.GetFullPath(ruta);
    }

    public static bool EsRaizValida(string ruta) =>
        Directory.Exists(Path.Combine(ruta, "img"))
        && Directory.Exists(Path.Combine(ruta, "productos"));

    public static string? EncontrarDesde(string inicio)
    {
        var dir = new DirectoryInfo(Path.TrimEndingDirectorySeparator(inicio));
        while (dir is not null)
        {
            if (EsRaizValida(dir.FullName))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public IReadOnlyList<string> ListarCarpetasImagen()
    {
        if (!Directory.Exists(ImgDir))
        {
            return [];
        }

        return Directory.EnumerateDirectories(ImgDir)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return !CarpetasOmitidas.Contains(name)
                       && Directory.Exists(Path.Combine(d, "full"));
            })
            .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
    }

    public IReadOnlyList<string> ListarFuentes()
    {
        if (!Directory.Exists(FuenteDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(FuenteDir)
            .Where(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string RutaCarpetaImg(string nombreCarpeta) => Path.Combine(ImgDir, nombreCarpeta);

    public string RutaFull(string nombreCarpeta) => Path.Combine(RutaCarpetaImg(nombreCarpeta), "full");
}
