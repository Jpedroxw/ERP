@echo off
rem Publica os componentes sem precisar liberar scripts no Windows (vale so para esta execucao).
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0publicar.ps1" %*
if errorlevel 1 (echo. & echo A PUBLICACAO FALHOU - veja a mensagem acima.) else (echo. & echo Publicacao concluida: pasta publicar.)
pause
