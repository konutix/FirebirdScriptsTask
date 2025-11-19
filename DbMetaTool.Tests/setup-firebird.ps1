# -------------------------------------------------------------
# Pobiera Firebird 5.0.3 (Windows x64) i przygotowuje folder fb5
# w trybie Embedded (fbclient.dll + plugins + ICU)
# -------------------------------------------------------------

$ErrorActionPreference = "Stop"

# Oficjalny ZIP (pełna instalacja Windows x64)
$firebirdUrl = "https://sourceforge.net/projects/firebird/files/v5.0.3/Firebird-5.0.3.1683-0-windows-x64.zip/download"

# Folder docelowy
$targetDir = Join-Path $PSScriptRoot "fb5"
$tempZip   = Join-Path $PSScriptRoot "fb5_download.zip"

Write-Host ">>> Przygotowywanie Firebird 5.0.3 Embedded-like runtime..." -ForegroundColor Cyan

# 1) Reset folderu fb5
if (Test-Path $targetDir) {
    Write-Host ">>> Usuwanie istniejącego folderu fb5..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $targetDir
}
New-Item -ItemType Directory -Path $targetDir | Out-Null

# 2) Pobranie pakietu
Write-Host ">>> Pobieranie Firebird 5.0.3 (curl)..."
curl.exe -L $firebirdUrl -o $tempZip

# 3) Rozpakowanie do folderu tymczasowego
$tempExtract = Join-Path $PSScriptRoot "fb5_extract"

if (Test-Path $tempExtract) {
    Remove-Item -Recurse -Force $tempExtract
}
New-Item -ItemType Directory -Path $tempExtract | Out-Null

Write-Host ">>> Rozpakowywanie ZIP..."
Expand-Archive -Path $tempZip -DestinationPath $tempExtract

# Ustwiamy katalog główny Firebirda
$fbRoot = $tempExtract

# Sprawdzamy, czy pliki Firebirda rzeczywiście istnieja
if (-not (Test-Path (Join-Path $fbRoot "fbclient.dll"))) {
    throw "Nie znaleziono fbclient.dll w rozpakowanym pakiecie Firebirda! Struktura ZIP mogła ulec zmianie."
}

# Katalog pluginow w rozpakowanym Firebird
$pluginSource = Join-Path $fbRoot "plugins"

if (-not (Test-Path $pluginSource)) {
    throw "Nie znaleziono katalogu 'plugins' w rozpakowanym Firebird (szukano: $pluginSource)"
}

# 4) Kopiowanie niezbednych plikow dla Embedded
Write-Host ">>> Kopiowanie minimalnego runtime (Embedded)..."

# Pliki główne
$mainFiles = @(
    "fbclient.dll",
    "firebird.msg",
    "firebird.conf",
    "icudt*.dll",
    "icuuc*.dll",
    "icuin*.dll"
)

foreach ($pattern in $mainFiles) {
    Get-ChildItem -Path $fbRoot -Filter $pattern |
        Copy-Item -Destination $targetDir -Force
}

# DLL-e pluginów (wymagany engine12.dll)
New-Item -ItemType Directory -Path (Join-Path $targetDir "plugins") | Out-Null

Get-ChildItem -Path $pluginSource -Filter "*.dll" |
    Copy-Item -Destination (Join-Path $targetDir "plugins") -Force

# 5) Sprzatanie
Write-Host ">>> Czyszczenie..."
Remove-Item $tempZip -Force
Remove-Item $tempExtract -Recurse -Force

# 6) Raport
if (Test-Path (Join-Path $targetDir "fbclient.dll")) {
    Write-Host ">>> Firebird Embedded runtime jest gotowy w: $targetDir" -ForegroundColor Green
} else {
    throw "Blad: fbclient.dll nie zostal skopiowany!"
}
