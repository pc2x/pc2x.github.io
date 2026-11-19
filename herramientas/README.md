# Herramientas del catálogo en línea

## CatalogoOnlineManager (recomendado)

App WinForms con:

- **Imágenes** — optimiza `img/{carpeta}/full/` → `opt/` (WebP) y regenera índices
- **Productos** — genera `productos/v/.../catalogo.json.gz` desde `productos/fuente/`
- **Importar ZIP** — toma el ZIP de LethorPos (Exportar → Catálogo en línea), copia imágenes a `full/`, optimiza con force y actualiza el catálogo

### Publicar el .exe

El `.exe` **no se sube a git** (pesa >100 MB y GitHub lo rechaza). Generarlo en local:

```bat
publicar-catalogo-online-manager.cmd
```

Queda en `herramientas\CatalogoOnlineManager.exe` (ignorado por `.gitignore`). Al abrirlo detecta la raíz del sitio (`img/` + `productos/`); si no, usa **Cambiar carpeta…**. Requiere publicar de nuevo tras cambios en el código.

### Flujo habitual tras exportar desde LethorPos

1. En LethorPos: Exportar → Catálogo en línea (activa **Incluir los que ya están en línea** si quieres actualizar existentes).
2. Abre `CatalogoOnlineManager.exe` → pestaña **Importar ZIP**.
3. Elige el ZIP y la carpeta bajo `img/` (por defecto `General`).
4. Revisa el log → commit/push a `main`.

## CLI legado (opcional)

Siguen disponibles para scripts:

- `img\ImageOptimizer.exe`
- `productos\CatalogoProductosGenerador.exe`
- `actualizar-catalogo-desde-zip.cmd`
- `publicar-optimizador.cmd` / `publicar-generador-productos.cmd`
