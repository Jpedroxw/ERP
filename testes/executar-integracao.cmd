@echo off
setlocal
cd /d "%~dp0.."
if "%ERP_TEST_URL%"=="" (
  echo Configure ERP_TEST_URL para a URL HTTPS da empresa ficticia bot...
  pause
  exit /b 2
)
if "%ERP_TEST_LOGIN%"=="" (
  echo Configure ERP_TEST_LOGIN com o login da empresa ficticia.
  pause
  exit /b 2
)
dotnet run -c Release --project testes\ProjetoJP.TestesIntegracao\ProjetoJP.TestesIntegracao.csproj
pause
