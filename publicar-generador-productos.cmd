@echo off
setlocal

rem Publica CatalogoProductosGenerador.exe (single-file, self-contained) y copia solo el .exe a productos\.
rem Las variables llevan prefijo PRODGEN_ porque MSBuild lee las variables de entorno como
rem propiedades (p. ej. OUTDIR redirigiria la salida del build a productos\).
set "PRODGEN_ROOT=%~dp0"
set "PRODGEN_PROJECT=%PRODGEN_ROOT%productos\generador\CatalogoProductosGenerador.csproj"
set "PRODGEN_DEST=%PRODGEN_ROOT%productos"
set "PRODGEN_STAGE=%TEMP%\CatalogoProductosGenerador_publish"

tasklist /FI "IMAGENAME eq CatalogoProductosGenerador.exe" | find /I "CatalogoProductosGenerador.exe" >nul
if not errorlevel 1 (
    echo CatalogoProductosGenerador.exe se esta ejecutando. Cierralo antes de publicar.
    exit /b 1
)

if exist "%PRODGEN_STAGE%" rmdir /s /q "%PRODGEN_STAGE%"

dotnet publish "%PRODGEN_PROJECT%" -c Release -o "%PRODGEN_STAGE%"
if errorlevel 1 (
    echo.
    echo ERROR: la publicacion fallo.
    exit /b 1
)

copy /y "%PRODGEN_STAGE%\CatalogoProductosGenerador.exe" "%PRODGEN_DEST%\CatalogoProductosGenerador.exe" >nul
if errorlevel 1 (
    echo.
    echo ERROR: no se pudo copiar CatalogoProductosGenerador.exe a %PRODGEN_DEST%.
    exit /b 1
)

rmdir /s /q "%PRODGEN_STAGE%"

echo.
echo Listo: %PRODGEN_DEST%\CatalogoProductosGenerador.exe
endlocal
