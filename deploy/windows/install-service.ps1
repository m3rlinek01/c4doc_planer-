#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Publikuje C4 GuestPass (self-contained, win-x64) i instaluje go jako usługę Windows.

.DESCRIPTION
    Aplikacja obsługuje tryb usługi Windows (UseWindowsService). Skrypt dodatkowo sprawdza obecność
    Microsoft.Extensions.Hosting.WindowsServices.dll w publikacji (kontrola spójności).

    Co robi skrypt:
      - dotnet publish -c Release -r win-x64 --self-contained -p:C4SdkVersion=<wersja>
      - kopiuje pliki do -InstallDir (istniejący appsettings.Production.json zostaje nienaruszony)
      - tworzy katalog danych (-DataDir) na bazę SQLite i pocztę w trybie Pickup
      - rejestruje usługę (konto wirtualne "NT SERVICE\<nazwa>", start automatyczny opóźniony,
        restart po awarii) i ustawia jej zmienne środowiskowe (ASPNETCORE_ENVIRONMENT, ASPNETCORE_URLS,
        bezwzględne ścieżki bazy i poczty)
      - ogranicza dostęp do appsettings.Production.json (zawiera hasła) do Administratorów, SYSTEM i konta usługi
      - opcjonalnie otwiera port w Zaporze Windows (-OpenFirewall)

.PARAMETER C4SdkVersion
    None (tylko symulacja C4), 2024 lub 2026 – musi odpowiadać wersji serwera C4.

.PARAMETER C4SdkPackageVersion
    Opcjonalnie dokładna wersja pakietów Gamanet, np. 2024.0.0.512.

.EXAMPLE
    .\install-service.ps1 -C4SdkVersion 2024 -Port 8080 -OpenFirewall

.EXAMPLE
    # aktualizacja: ten sam skrypt – zatrzymuje usługę, podmienia pliki, uruchamia ponownie
    .\install-service.ps1 -C4SdkVersion 2024
#>
[CmdletBinding()]
param(
    [ValidateSet('None', '2024', '2026')]
    [string] $C4SdkVersion = 'None',

    [string] $C4SdkPackageVersion = '',

    [string] $ServiceName = 'C4GuestPass',

    [string] $DisplayName = 'C4 GuestPass',

    [string] $InstallDir = (Join-Path $env:ProgramFiles 'C4GuestPass'),

    [string] $DataDir = (Join-Path $env:ProgramData 'C4GuestPass'),

    [ValidateRange(1, 65535)]
    [int] $Port = 8080,

    [string] $Environment = 'Production',

    # Ścieżka do katalogu głównego repozytorium (domyślnie dwa poziomy nad skryptem).
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,

    # Nie publikuj – użyj gotowych plików z -PublishDir (np. zbudowanych na innej maszynie).
    [switch] $SkipPublish,

    [string] $PublishDir = '',

    [switch] $OpenFirewall,

    # Instaluj mimo braku integracji z SCM (tylko jeśli wiesz, co robisz).
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string] $msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

$project = Join-Path $RepoRoot 'src\C4GuestPass.Web\C4GuestPass.Web.csproj'
$exeName = 'C4GuestPass.Web.exe'
$serviceAccount = "NT SERVICE\$ServiceName"

# ------------------------------------------------------------------ 1. publikacja
if (-not $SkipPublish) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Nie znaleziono polecenia dotnet. Zainstaluj .NET 8 SDK albo użyj -SkipPublish -PublishDir <katalog>.'
    }
    if (-not (Test-Path $project)) { throw "Nie znaleziono projektu: $project (sprawdź -RepoRoot)." }

    $PublishDir = Join-Path ([IO.Path]::GetTempPath()) ("c4guestpass-publish-" + [Guid]::NewGuid().ToString('N'))
    $publishArgs = @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-o', $PublishDir,
        "-p:C4SdkVersion=$C4SdkVersion"
    )
    if ($C4SdkPackageVersion) { $publishArgs += "-p:C4SdkPackageVersion=$C4SdkPackageVersion" }

    Write-Step "dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish zakończył się kodem $LASTEXITCODE." }
}
elseif (-not $PublishDir -or -not (Test-Path (Join-Path $PublishDir $exeName))) {
    throw "Przy -SkipPublish podaj -PublishDir z plikiem $exeName."
}

# ------------------------------------------------------------------ 2. kontrola integracji z SCM
$hasWinSvc = Test-Path (Join-Path $PublishDir 'Microsoft.Extensions.Hosting.WindowsServices.dll')
if (-not $hasWinSvc) {
    $msg = @"
Publikacja nie zawiera Microsoft.Extensions.Hosting.WindowsServices.dll – aplikacja nie potrafi działać
jako usługa Windows (SCM zgłosi błąd 1053). Wprowadź zmianę opisaną w nagłówku skryptu (Get-Help .\install-service.ps1 -Full)
albo wdróż aplikację w IIS (README.md, "Wdrożenie – IIS").
"@
    if (-not $Force) { throw $msg }
    Write-Warning $msg
}

# Plik deweloperski (hasło demo, dane demo) nie powinien trafić na serwer.
Remove-Item (Join-Path $PublishDir 'appsettings.Development.json') -ErrorAction SilentlyContinue

# ------------------------------------------------------------------ 3. zatrzymanie istniejącej usługi
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Step "Zatrzymuję usługę $ServiceName"
    Stop-Service -Name $ServiceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

# ------------------------------------------------------------------ 4. kopiowanie plików
Write-Step "Kopiuję pliki do $InstallDir"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
# Publikacja nie zawiera appsettings.Production.json, więc lokalna konfiguracja nie zostanie nadpisana.
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $InstallDir -Recurse -Force
if (-not $SkipPublish) { Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue }

$prodConfig = Join-Path $InstallDir 'appsettings.Production.json'
if (-not (Test-Path $prodConfig)) {
    $example = Join-Path $RepoRoot 'deploy\appsettings.Production.example.json'
    if (Test-Path $example) {
        Copy-Item $example $prodConfig
        Write-Warning "Utworzono $prodConfig z przykładu – UZUPEŁNIJ go (serwer C4, foldery osób, SMTP) przed użyciem."
    }
    else {
        Write-Warning "Brak $prodConfig – utwórz go na podstawie deploy\appsettings.Production.example.json."
    }
}

New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $DataDir 'mail') -Force | Out-Null

# ------------------------------------------------------------------ 5. rejestracja usługi
$binPath = '"' + (Join-Path $InstallDir $exeName) + '"'
if (-not $existing) {
    Write-Step "Rejestruję usługę $ServiceName"
    New-Service -Name $ServiceName -DisplayName $DisplayName -BinaryPathName $binPath `
        -Description 'C4 GuestPass – zaproszenia gości z kodem QR dla Gamanet C4' -StartupType Automatic | Out-Null
}
else {
    & sc.exe config $ServiceName binPath= $binPath | Out-Null
}

# Konto wirtualne usługi (bez hasła, bez uprawnień administratora), start automatyczny opóźniony,
# restart po awarii: 3 próby co 60 s, licznik zerowany po dobie.
& sc.exe config $ServiceName obj= $serviceAccount start= delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe config zakończył się kodem $LASTEXITCODE." }
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

# Zmienne środowiskowe tylko dla tej usługi (HKLM\SYSTEM\CurrentControlSet\Services\<nazwa>\Environment).
# Ścieżki muszą być bezwzględne: aplikacja liczy ścieżki względne od katalogu roboczego, który dla usługi
# to C:\Windows\System32.
$envVars = @(
    "ASPNETCORE_ENVIRONMENT=$Environment",
    "ASPNETCORE_URLS=http://+:$Port",
    "GuestPass__DatabasePath=$(Join-Path $DataDir 'guestpass.db')",
    "Mail__PickupDirectory=$(Join-Path $DataDir 'mail')"
)
$svcKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $svcKey -Name 'Environment' -PropertyType MultiString -Value $envVars -Force | Out-Null

# ------------------------------------------------------------------ 6. uprawnienia
Write-Step 'Ustawiam uprawnienia (katalog danych, plik konfiguracji)'
& icacls.exe $DataDir /grant "${serviceAccount}:(OI)(CI)M" /T /Q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls (DataDir) zakończył się kodem $LASTEXITCODE." }
& icacls.exe $InstallDir /grant "${serviceAccount}:(OI)(CI)RX" /T /Q | Out-Null
if (Test-Path $prodConfig) {
    # Hasła w pliku -> tylko Administratorzy, SYSTEM i konto usługi (SID-y, niezależne od języka systemu).
    & icacls.exe $prodConfig /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' "${serviceAccount}:R" /Q | Out-Null
}

# Źródło Dziennika zdarzeń – konto wirtualne usługi nie ma prawa go utworzyć samo.
# (AddWindowsService loguje do dziennika Application ze źródłem = nazwa aplikacji, domyślnie od poziomu Warning.)
$eventSource = 'C4GuestPass.Web'
if (-not [System.Diagnostics.EventLog]::SourceExists($eventSource)) {
    New-EventLog -LogName Application -Source $eventSource
}

# ------------------------------------------------------------------ 7. zapora
if ($OpenFirewall) {
    $ruleName = "$DisplayName (TCP $Port)"
    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        Write-Step "Dodaję regułę zapory: $ruleName"
        New-NetFirewallRule -DisplayName $ruleName -Group $DisplayName -Direction Inbound -Protocol TCP `
            -LocalPort $Port -Action Allow -Profile Domain, Private | Out-Null
    }
}

# ------------------------------------------------------------------ 8. start
Write-Step "Uruchamiam usługę $ServiceName"
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

Write-Host ''
Write-Host "Gotowe. Aplikacja: http://$($env:COMPUTERNAME):$Port/" -ForegroundColor Green
Write-Host "Konfiguracja:      $prodConfig"
Write-Host "Dane (SQLite):     $DataDir"
Write-Host 'Logi:              Podgląd zdarzeń -> Dziennik aplikacji, źródło "C4GuestPass.Web"'
Write-Host 'Hasło startowe administratora (gdy Bootstrap:AdminPassword puste) znajdziesz w dzienniku zdarzeń.'
Write-Host 'Zalecane: wystaw aplikację przez HTTPS (IIS/ARR, reverse proxy) – nie bezpośrednio po HTTP.'
