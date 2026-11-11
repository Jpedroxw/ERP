@echo off
setlocal
cd /d "%~dp0.."
set FALHAS=0
echo === Validacoes de campos ===
dotnet run -c Release --project testes\ProjetoJP.TestesValidacao\ProjetoJP.TestesValidacao.csproj
if errorlevel 1 set /a FALHAS+=1
echo.
echo === Pesquisa do estoque ===
dotnet run -c Release --project testes\ProjetoJP.TestesPesquisaEstoque\ProjetoJP.TestesPesquisaEstoque.csproj
if errorlevel 1 set /a FALHAS+=1
echo.
echo === Importacao ===
dotnet run -c Release --project testes\ProjetoJP.TestesImportacao\ProjetoJP.TestesImportacao.csproj
if errorlevel 1 set /a FALHAS+=1
echo.
if %FALHAS%==0 (echo TODOS OS TESTES PASSARAM) else (echo %FALHAS% GRUPO^(S^) DE TESTE FALHARAM)
pause
