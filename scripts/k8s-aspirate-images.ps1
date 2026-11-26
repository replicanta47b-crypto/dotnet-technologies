# Lab 10, part 1: builds local images for aspirate-output (aspirate generate --skip-build skips build AND push).
# 'dotnet publish /t:PublishContainer' without ContainerRegistry puts the image into the LOCAL Docker daemon (no push).
# Names match aspirate-output: docker.io/platform/<name>:latest (docker.io/ prefix is implicit, so we tag platform/<name>:latest).
# Run from the repository root:  powershell -ExecutionPolicy Bypass -File .\scripts\k8s-aspirate-images.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$projects = [ordered]@{
    'payments-api'    = 'Services/Payments/Payments.Api/Payments.Api.csproj'
    'billing-api'     = 'Services/Billing/Billing.Api/Billing.Api.csproj'
    'disputes-api'    = 'Services/Disputes/Disputes.WebApi/Disputes.WebApi.csproj'
    'auth-api'        = 'Services/Auth/Auth.Api/Auth.Api.csproj'
    'settlements-api' = 'Services/Settlements/Settlements.Api/Settlements.Api.csproj'
    'aggregator'      = 'Platform.Aggregator/Platform.Aggregator.csproj'
    'gateway'         = 'Platform.ApiGateway/Platform.ApiGateway.csproj'
}
foreach ($name in $projects.Keys) {
    Write-Host "== $name =="
    dotnet publish $projects[$name] -c Release -r linux-x64 --self-contained false /t:PublishContainer `
        -p:ContainerRepository=platform/$name -p:ContainerImageTag=latest
    if ($LASTEXITCODE -ne 0) { throw "build failed: $name" }
}
docker images --filter "reference=platform/*"
