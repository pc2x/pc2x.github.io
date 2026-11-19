@echo off
setlocal

set "EDGE=C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" set "EDGE=C:\Program Files\Microsoft\Edge\Application\msedge.exe"
if not exist "%EDGE%" (
  echo No se encontro Microsoft Edge.
  pause
  exit /b 1
)

set "CARPETA=%~dp0"
set "URL=%CARPETA:\=/%"

"%EDGE%" --headless --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --virtual-time-budget=4000 --window-size=1640,924 --screenshot="%CARPETA%portada.png" "file:///%URL%portada.html"

echo Generado: %CARPETA%portada.png
pause
