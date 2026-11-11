@echo off
rem Projeto JP: apaga as pastas com os nomes antigos (Erp.*), que foram substituidas pelas novas (ProjetoJP.*).
rem Rode uma vez, com o Visual Studio fechado. Depois pode apagar este arquivo.
cd /d "%~dp0.."
if not exist "src\ProjetoJP.Pdv\ProjetoJP.Pdv.csproj" goto semNovas
if not exist "src\ProjetoJP.Gestao\ProjetoJP.Gestao.csproj" goto semNovas
if not exist "ProjetoJP.slnx" goto semNovas
echo Vai apagar desta pasta:
echo   src\Erp.Shared, Erp.Controles, Erp.Api, Erp.Pdv, Erp.Retaguarda, Erp.Master, Erp.Instalador
echo   testes\Erp.Testes...
echo   ErpNaoFiscal.slnx
echo   deploy\atualizar-servidor.cmd, .ps1 e .sh (scripts que foram desfeitos)
echo.
echo O codigo agora fica em src\ProjetoJP.* e a solucao e o ProjetoJP.slnx.
choice /c SN /m "Confirmar"
if errorlevel 2 goto fim
for %%P in (Shared Controles Api Pdv Retaguarda Master Instalador) do if exist "src\Erp.%%P" rmdir /s /q "src\Erp.%%P"
for %%P in (TestesBanco TestesImportacao TestesIntegracao TestesPesquisaEstoque TestesRegrasV8 TestesValidacao) do if exist "testes\Erp.%%P" rmdir /s /q "testes\Erp.%%P"
if exist "ErpNaoFiscal.slnx" del /q "ErpNaoFiscal.slnx"
for %%F in (cmd ps1 sh) do if exist "deploy\atualizar-servidor.%%F" del /q "deploy\atualizar-servidor.%%F"
echo.
echo Pronto. Abra o ProjetoJP.slnx.
goto fim
:semNovas
echo As pastas novas (src\ProjetoJP.*) ou o ProjetoJP.slnx nao foram encontrados. Nada foi apagado.
:fim
pause
