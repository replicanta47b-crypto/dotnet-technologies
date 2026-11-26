# Lab 10, part 1: builds LOCAL images referenced by aspirate-output (docker.io/platform/<name>:latest).
# aspirate generate --skip-build skips build AND push, so the images are built here with docker (no registry push).
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File .\scripts\k8s-aspirate-images.ps1
param([string[]] $Only = @())   # e.g. -Only payments-api  (rebuild just one image)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$projects = [ordered]@{
    'payments-api'    = @('Services/Payments/Payments.Api/Payments.Api.csproj', 'Payments.Api.dll')
    'billing-api'     = @('Services/Billing/Billing.Api/Billing.Api.csproj', 'Billing.Api.dll')
    'disputes-api'    = @('Services/Disputes/Disputes.WebApi/Disputes.WebApi.csproj', 'Disputes.WebApi.dll')
    'auth-api'        = @('Services/Auth/Auth.Api/Auth.Api.csproj', 'Auth.Api.dll')
    'settlements-api' = @('Services/Settlements/Settlements.Api/Settlements.Api.csproj', 'Settlements.Api.dll')
    'aggregator'      = @('Platform.Aggregator/Platform.Aggregator.csproj', 'Platform.Aggregator.dll')
    'gateway'         = @('Platform.ApiGateway/Platform.ApiGateway.csproj', 'Platform.ApiGateway.dll')
}
foreach ($name in $projects.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $name) { continue }
    Write-Host "== $name =="
    docker build -f docker/Dockerfile.service --build-arg PROJECT=$($projects[$name][0]) --build-arg DLL=$($projects[$name][1]) -t platform/${name}:latest .
    if ($LASTEXITCODE -ne 0) { throw "docker build failed: $name" }
}
docker images | Select-String "platform/"
