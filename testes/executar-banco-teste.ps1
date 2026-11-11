$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
if (-not $env:ERP_TEST_PG_CONN -or $env:ERP_TESTE_BANCO_CONFIRMAR -ne "SIM_TESTE") {
    Write-Host "Configure ERP_TEST_PG_CONN (banco ficticio) e ERP_TESTE_BANCO_CONFIRMAR=SIM_TESTE antes de executar." -ForegroundColor Yellow
    exit 2
}
dotnet run -c Release --project (Join-Path $PSScriptRoot "ProjetoJP.TestesBanco/ProjetoJP.TestesBanco.csproj")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
