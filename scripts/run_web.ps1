# Lance l'application Blazor sans dependre du PATH systeme.
$dotnet = "C:\Program Files\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) {
    Write-Error "SDK .NET introuvable : $dotnet"
    Write-Host ""
    Write-Host "Installez .NET 8 SDK : https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
}

$project = Join-Path $PSScriptRoot "..\src\Axioplan.GammesNomenclatures.Web\Axioplan.GammesNomenclatures.Web.csproj"
$port = 5280

# Libere le port si une instance precedente tourne encore.
# Note: $PID est une variable automatique PowerShell en lecture seule — ne pas l'utiliser.
$listeners = netstat -ano | Select-String ":$port\s" | Select-String "LISTENING"
$processIds = @(
    $listeners | ForEach-Object {
        ($_ -split '\s+')[-1]
    } | Where-Object { $_ -match '^\d+$' } | Select-Object -Unique
)

foreach ($processId in $processIds) {
    Write-Host "Arret du processus $processId qui occupe le port $port..."
    taskkill /PID $processId /F 2>$null | Out-Null
}

# Securite: arreter aussi toute instance Web encore en memoire (DLL verrouillees).
Get-Process -Name "Axioplan.GammesNomenclatures.Web" -ErrorAction SilentlyContinue |
    ForEach-Object {
        Write-Host "Arret de l'instance Web (PID $($_.Id))..."
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }

Start-Sleep -Seconds 1

Write-Host "Demarrage sur http://localhost:$port"
Write-Host "(Ctrl+C pour arreter)"
Write-Host ""

& $dotnet run --project $project @args
if ($LASTEXITCODE -eq 2147516566) {
    Write-Host ""
    Write-Host "Runtime .NET 8 manquant. Installez ASP.NET Core Runtime 8.0 :"
    Write-Host "https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host ""
    Write-Host "Ou via winget :"
    Write-Host "  winget install Microsoft.DotNet.AspNetCore.8"
}
