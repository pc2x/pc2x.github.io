@echo off
setlocal

rem Publica CatalogoOnlineManager.exe (single-file, self-contained) en herramientas\.
set "COM_ROOT=%~dp0"
set "COM_PROJECT=%COM_ROOT%herramientas\CatalogoOnlineManager\CatalogoOnlineManager.csproj"
set "COM_DEST=%COM_ROOT%herramientas"
set "COM_STAGE=%TEMP%\CatalogoOnlineManager_publish"

tasklist /FI "IMAGENAME eq CatalogoOnlineManager.exe" | find /I "CatalogoOnlineManager.exe" >nul
if not errorlevel 1 (
    echo CatalogoOnlineManager.exe se esta ejecutando. Cierralo antes de publicar.
    exit /b 1
)

if exist "%COM_STAGE%" rmdir /s /q "%COM_STAGE%"

dotnet publish "%COM_PROJECT%" -c Release -o "%COM_STAGE%"
if errorlevel 1 (
    echo.
    echo ERROR: la publicacion fallo.
    exit /b 1
)

copy /y "%COM_STAGE%\CatalogoOnlineManager.exe" "%COM_DEST%\CatalogoOnlineManager.exe" >nul
if errorlevel 1 (
    echo.
    echo ERROR: no se pudo copiar CatalogoOnlineManager.exe a %COM_DEST%.
    exit /b 1
)

rmdir /s /q "%COM_STAGE%"

echo.
echo Listo: %COM_DEST%\CatalogoOnlineManager.exe
endlocal
