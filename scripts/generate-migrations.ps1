<#
 Лаб. 09: генерує EF Core міграції для чотирьох контекстів і комітить їх у гілку.
   1) Auth.Api            — ApplicationDbContext (ASP.NET Identity + RefreshTokens)       → IdentityDb
   2) Platform.IdentityServer — PersistedGrantDbContext (operational: grants, ключі)      → IdentityServerDb
   3) Platform.IdentityServer — ConfigurationDbContext (clients, resources, scopes)       → IdentityServerDb
   4) Platform.IdentityServer — ApplicationDbContext (ASP.NET Identity користувачі IS)    → IdentityServerDb
 Запуск з кореня репозиторію:  .\scripts\generate-migrations.ps1
 З'єднання з БД не потрібне: міграції лише описують схему. Скрипт безпечно запускати повторно (існуючі міграції пропускаються).
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

Write-Host '== dotnet-ef (інструмент міграцій) ==' -ForegroundColor Cyan
dotnet tool update --global dotnet-ef 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { dotnet tool install --global dotnet-ef }

function Add-Migration($project, $context, $name, $outDir) {
    $dir = Join-Path $project $outDir
    if (Test-Path $dir) { Write-Host "  пропуск: $dir вже існує" -ForegroundColor DarkGray; return }
    Write-Host "== $project / $context -> $outDir ==" -ForegroundColor Cyan
    dotnet ef migrations add $name --project $project --context $context --output-dir $outDir
    if ($LASTEXITCODE -ne 0) { throw "Міграція $context не створена" }
}

Add-Migration 'Services/Auth/Auth.Api'      'ApplicationDbContext'    'InitialIdentity'      'Data/Migrations'
Add-Migration 'Platform.IdentityServer'     'PersistedGrantDbContext' 'InitialPersistedGrant' 'Data/Migrations/PersistedGrantDb'
Add-Migration 'Platform.IdentityServer'     'ConfigurationDbContext'  'InitialConfiguration'  'Data/Migrations/ConfigurationDb'
Add-Migration 'Platform.IdentityServer'     'ApplicationDbContext'    'InitialIdentityUsers'  'Data/Migrations/ApplicationDb'

git add Services/Auth/Auth.Api/Data/Migrations Platform.IdentityServer/Data/Migrations
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git commit -m "Lab 9: EF Core migrations (IdentityDb, IdentityServerDb: PersistedGrant, Configuration, Application)"
    git push origin lab-09
    Write-Host 'Міграції закомічено й відправлено в lab-09.' -ForegroundColor Green
} else {
    Write-Host 'Нових міграцій немає.' -ForegroundColor Yellow
}
