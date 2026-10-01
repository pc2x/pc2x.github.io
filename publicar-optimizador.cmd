@echo off
setlocal

rem Publica ImageOptimizer.exe (single-file, self-contained) y copia solo el .exe a img\.
rem Las variables llevan prefijo IMGOPT_ porque MSBuild lee las variables de entorno como
rem propiedades (p. ej. OUTDIR redirigiria la salida del build a img\).
set "IMGOPT_ROOT=%~dp0"
set "IMGOPT_PROJECT=%IMGOPT_ROOT%img\optimizer\ImageOptimizer.csproj"
set "IMGOPT_DEST=%IMGOPT_ROOT%img"
set "IMGOPT_STAGE=%TEMP%\ImageOptimizer_publish"

tasklist /FI "IMAGENAME eq ImageOptimizer.exe" | find /I "ImageOptimizer.exe" >nul
if not errorlevel 1 (
    echo ImageOptimizer.exe se esta ejecutando. Cierralo antes de publicar.
    exit /b 1
)

if exist "%IMGOPT_STAGE%" rmdir /s /q "%IMGOPT_STAGE%"

dotnet publish "%IMGOPT_PROJECT%" -c Release -o "%IMGOPT_STAGE%"
if errorlevel 1 (
    echo.
    echo ERROR: la publicacion fallo.
    exit /b 1
)

copy /y "%IMGOPT_STAGE%\ImageOptimizer.exe" "%IMGOPT_DEST%\ImageOptimizer.exe" >nul
if errorlevel 1 (
    echo.
    echo ERROR: no se pudo copiar ImageOptimizer.exe a %IMGOPT_DEST%.
    exit /b 1
)

rmdir /s /q "%IMGOPT_STAGE%"

echo.
echo Listo: %IMGOPT_DEST%\ImageOptimizer.exe
endlocal
