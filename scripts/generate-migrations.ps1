<#
 Lab 9: generates EF Core migrations for four DbContexts and commits them to the branch.
   1) Auth.Api                - ApplicationDbContext (ASP.NET Identity + RefreshTokens)   -> IdentityDb
   2) Platform.IdentityServer - PersistedGrantDbContext                                    -> IdentityServerDb
   3) Platform.IdentityServer - ConfigurationDbContext                                     -> IdentityServerDb
   4) Platform.IdentityServer - ApplicationDbContext (IdentityServer users)                -> IdentityServerDb
 Run from the repository root. No DB connection is needed. Safe to re-run (existing migrations are skipped).
 NOTE: file is ASCII-only on purpose (Windows PowerShell 5 reads BOM-less UTF-8 as ANSI).
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

Write-Host '== dotnet-ef tool ==' -ForegroundColor Cyan
# dotnet-ef MUST be 9.x: version 10 needs the .NET 10 runtime, the platform targets .NET 9
dotnet tool uninstall --global dotnet-ef 2>&1 | Out-Null
dotnet tool install --global dotnet-ef --version "9.*"
if ($LASTEXITCODE -ne 0) { throw 'dotnet-ef 9.x was not installed' }

function Add-Migration($project, $context, $name, $outDir) {
    $dir = Join-Path $project $outDir
    if (Test-Path $dir) { Write-Host "  skip: $dir already exists" -ForegroundColor DarkGray; return }
    Write-Host "== $project / $context -> $outDir ==" -ForegroundColor Cyan
    dotnet ef migrations add $name --project $project --context $context --output-dir $outDir
    if ($LASTEXITCODE -ne 0) { throw "Migration for $context was not created" }
}

Add-Migration 'Services/Auth/Auth.Api'  'ApplicationDbContext'    'InitialIdentity'       'Data/Migrations'
Add-Migration 'Platform.IdentityServer' 'PersistedGrantDbContext' 'InitialPersistedGrant' 'Data/Migrations/PersistedGrantDb'
Add-Migration 'Platform.IdentityServer' 'ConfigurationDbContext'  'InitialConfiguration'  'Data/Migrations/ConfigurationDb'
Add-Migration 'Platform.IdentityServer' 'ApplicationDbContext'    'InitialIdentityUsers'  'Data/Migrations/ApplicationDb'

git add Services/Auth/Auth.Api/Data/Migrations Platform.IdentityServer/Data/Migrations
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git commit -m "Lab 9: EF Core migrations (IdentityDb, IdentityServerDb: PersistedGrant, Configuration, Application)"
    git push origin lab-09
    Write-Host 'Migrations committed and pushed to lab-09.' -ForegroundColor Green
} else {
    Write-Host 'No new migrations.' -ForegroundColor Yellow
}
