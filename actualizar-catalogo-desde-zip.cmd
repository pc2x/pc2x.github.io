@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Actualiza productos e imagenes del catálogo público a partir de un ZIP
rem exportado por LethorPos (Exportar → Catálogo en línea).
rem
rem Uso:
rem   actualizar-catalogo-desde-zip.cmd ruta\catalogo_online_yyyyMMdd_HHmmss.zip
rem   actualizar-catalogo-desde-zip.cmd ruta\archivo.zip General
rem
rem Las imagenes se copian a img\{carpeta}\full\ (por defecto General), se regenera
rem opt/ con --force y el CSV se aplica con CatalogoProductosGenerador --actualizar
rem sobre el resto de productos/fuente/.

set "ROOT=%~dp0"
set "ZIP=%~1"
set "CARPETA=%~2"
if "%CARPETA%"=="" set "CARPETA=General"

if "%ZIP%"=="" (
    echo Uso: %~nx0 ruta\catalogo_online_yyyyMMdd_HHmmss.zip [carpeta_img]
    echo Ejemplo: %~nx0 %%USERPROFILE%%\Downloads\catalogo_online_20261119_153000.zip
    echo          %~nx0 .\export.zip General
    exit /b 1
)

if not exist "%ZIP%" (
    echo ERROR: no existe el ZIP: %ZIP%
    exit /b 1
)

set "IMG_FULL=%ROOT%img\%CARPETA%\full"
set "OPTIMIZER=%ROOT%img\ImageOptimizer.exe"
set "GENERADOR=%ROOT%productos\CatalogoProductosGenerador.exe"
set "STAGE=%TEMP%\lethorpos_catalogo_zip_%RANDOM%"

if not exist "%OPTIMIZER%" (
    echo ERROR: falta %OPTIMIZER%
    echo Ejecuta publicar-optimizador.cmd primero.
    exit /b 1
)
if not exist "%GENERADOR%" (
    echo ERROR: falta %GENERADOR%
    echo Ejecuta publicar-generador-productos.cmd primero.
    exit /b 1
)

mkdir "%STAGE%" 2>nul
mkdir "%STAGE%\unzip" 2>nul

echo Descomprimiendo ZIP...
powershell -NoProfile -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%STAGE%\unzip' -Force"
if errorlevel 1 (
    echo ERROR: no se pudo descomprimir el ZIP.
    rmdir /s /q "%STAGE%" 2>nul
    exit /b 1
)

set "CSV="
for /r "%STAGE%\unzip" %%F in (*.csv) do (
    if not defined CSV set "CSV=%%F"
)

if not defined CSV (
    echo ERROR: el ZIP no contiene un CSV.
    rmdir /s /q "%STAGE%" 2>nul
    exit /b 1
)

echo CSV: %CSV%

mkdir "%IMG_FULL%" 2>nul
set "IMG_SRC="
if exist "%STAGE%\unzip\imagenes\" set "IMG_SRC=%STAGE%\unzip\imagenes"
if not defined IMG_SRC (
    for /d %%D in ("%STAGE%\unzip\*") do (
        if exist "%%D\imagenes\" set "IMG_SRC=%%D\imagenes"
    )
)

set "COPIADAS=0"
if defined IMG_SRC (
    echo Copiando imagenes a img\%CARPETA%\full\ ...
    for %%F in ("%IMG_SRC%\*.*") do (
        copy /y "%%F" "%IMG_FULL%\" >nul
        if not errorlevel 1 set /a COPIADAS+=1
    )
    echo Imagenes copiadas: !COPIADAS!
) else (
    echo Aviso: el ZIP no trae carpeta imagenes\; se actualizan solo datos del CSV.
)

echo.
echo Optimizando imagenes ^(ImageOptimizer --force %CARPETA%^)...
"%OPTIMIZER%" --force "%CARPETA%"
if errorlevel 1 (
    echo ERROR: ImageOptimizer fallo.
    rmdir /s /q "%STAGE%" 2>nul
    exit /b 1
)

echo.
echo Generando catalogo ^(CatalogoProductosGenerador --actualizar^)...
"%GENERADOR%" --actualizar "%CSV%"
if errorlevel 1 (
    echo ERROR: CatalogoProductosGenerador fallo.
    rmdir /s /q "%STAGE%" 2>nul
    exit /b 1
)

rmdir /s /q "%STAGE%" 2>nul

echo.
echo Listo. Revisa el diff de productos\ e img\ y haz commit/push a main.
endlocal
