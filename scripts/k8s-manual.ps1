# Lab 10, part 2+3: manual deployment to Docker Desktop Kubernetes (ASCII-only: Windows PowerShell 5 reads BOM-less UTF-8 as ANSI).
# Usage (from the repository root):
#   .\scripts\k8s-manual.ps1 -Action up        # build images, create Secrets, apply manifests
#   .\scripts\k8s-manual.ps1 -Action down      # delete everything created by 'up'
# Secrets are created with kubectl from variables, so they never get into YAML files in the repo.
param(
    [ValidateSet('up', 'down')] [string] $Action = 'up',
    [string] $SqlPassword = 'Lab10-Sql#Pass1234',                                   # dev value; SQL Server requires a complex password
    [string] $JwtKey = 'dev-only-secret-key-change-me-0123456789-abcdef'           # must equal the key of Auth Service (min 32 chars)
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

if ($Action -eq 'down') {
    kubectl delete -f k8s/manual/gateway, k8s/manual/billing-api, k8s/manual/auth-api, k8s/manual/infra --ignore-not-found
    kubectl delete secret sql-secrets billing-secrets auth-secrets gateway-secrets --ignore-not-found
    return
}

kubectl config use-context docker-desktop

Write-Host '== 1/4 docker build (images are visible to Docker Desktop Kubernetes without a registry) =='
docker build -t billing-api:v1 -f Services/Billing/Billing.Api/Dockerfile .
docker build -t auth-api:v1    -f Services/Auth/Auth.Api/Dockerfile .
docker build -t gateway:v1     -f Platform.ApiGateway/Dockerfile .

Write-Host '== 2/4 Secrets (kubectl create secret; idempotent via dry-run | apply) =='
function New-Secret($name, $pairs) {
    $args = @('create', 'secret', 'generic', $name)
    foreach ($k in $pairs.Keys) { $args += "--from-literal=$k=$($pairs[$k])" }
    $args += @('--dry-run=client', '-o', 'yaml')
    & kubectl @args | kubectl apply -f -
}
$sqlHost = 'Server=sql,1433;User Id=sa;Password=' + $SqlPassword + ';TrustServerCertificate=true;'
New-Secret 'sql-secrets'     @{ SA_PASSWORD = $SqlPassword }
New-Secret 'billing-secrets' @{ 'ConnectionStrings__BillingDb' = $sqlHost + 'Database=BillingDb'; 'Auth__AuthService__SecretKey' = $JwtKey }
New-Secret 'auth-secrets'    @{ 'ConnectionStrings__IdentityDb' = $sqlHost + 'Database=IdentityDb'; 'Auth__AuthService__SecretKey' = $JwtKey }
New-Secret 'gateway-secrets' @{ 'Auth__AuthService__SecretKey' = $JwtKey }

Write-Host '== 3/4 infrastructure + services =='
kubectl apply -f k8s/manual/infra
kubectl rollout status deploy/sql --timeout=240s
kubectl apply -f k8s/manual/billing-api -f k8s/manual/auth-api -f k8s/manual/gateway

Write-Host '== 4/4 waiting for pods =='
kubectl rollout status deploy/billing-api --timeout=300s
kubectl rollout status deploy/auth-api --timeout=300s
kubectl rollout status deploy/gateway --timeout=300s
kubectl get pods,services
Write-Host ''
Write-Host 'Next: kubectl port-forward service/gateway 8080:80   (in a second terminal), then see docs/lab-10/README.md'
