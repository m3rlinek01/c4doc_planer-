<#
.SYNOPSIS
  Kopiuje pakiety Gamanet C4 SDK z instalacji na Windows do deploy\c4-sdk – do budowania obrazu Docker z C4 2024.

.DESCRIPTION
  Pakiety są licencjonowane: katalog deploy\c4-sdk jest w .gitignore i nie trafia do repozytorium.
  Potem (na komputerze z Dockerem, np. serwerze Linux – skopiuj tam repozytorium razem z deploy/c4-sdk):
    cd deploy && docker compose build --build-arg C4_SDK_VERSION=2024   (albo C4_SDK_VERSION=2024 w deploy/.env)
#>
param([string] $C4SdkLocalFeed = 'C:\Program Files (x86)\Gamanet\C4 SDK')
$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot 'c4-sdk'
if (-not (Test-Path (Join-Path $C4SdkLocalFeed 'gamanet.c4.simpleclient'))) { throw "Nie znaleziono C4 SDK w '$C4SdkLocalFeed'." }
Get-ChildItem $dest -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Get-ChildItem $C4SdkLocalFeed -Directory | ForEach-Object { Copy-Item $_.FullName $dest -Recurse -Force }
Get-ChildItem $dest -Recurse -Filter *.nupkg | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "Gotowe: $dest" -ForegroundColor Green
