# Exporta los HTML de esta carpeta a PNG con Microsoft Edge en modo headless.
# Uso: powershell -ExecutionPolicy Bypass -File facebook_resources\exportar.ps1

$ErrorActionPreference = "Stop"
$carpeta = $PSScriptRoot

$edge = @(
  "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
  "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $edge) { throw "No se encontró Microsoft Edge." }

$piezas = @(
  @{ Html = "perfil.html";      Png = "perfil.png";      Ancho = 1080; Alto = 1080 },
  @{ Html = "portada.html";     Png = "portada.png";     Ancho = 1640; Alto = 924 },
  @{ Html = "guia-zonas.html";  Png = "guia-zonas.png";  Ancho = 1640; Alto = 1000 }
)

foreach ($p in $piezas) {
  $html = Join-Path $carpeta $p.Html
  if (-not (Test-Path $html)) { continue }
  $png = Join-Path $carpeta $p.Png
  $url = "file:///" + ($html -replace "\\", "/")
  $argumentos = @(
    "--headless", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
    "--virtual-time-budget=4000", "--window-size=$($p.Ancho),$($p.Alto)",
    "--screenshot=$png", $url
  )
  Start-Process -FilePath $edge -ArgumentList $argumentos -Wait -NoNewWindow -RedirectStandardError "$env:TEMP\edge-fb.log"
  Write-Host "Generado: $png"
}
