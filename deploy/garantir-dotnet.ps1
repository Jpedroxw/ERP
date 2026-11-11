# ERP - garante o SDK .NET 10 para gerar os programas (usado pelo publicar.ps1).
# Se o computador nao tiver o SDK 10, baixa e instala sozinho, direto da Microsoft, na pasta do usuario
# (%LOCALAPPDATA%\Microsoft\dotnet). Nao precisa de administrador e nao mexe em outro .NET instalado.
# Este arquivo usa somente caracteres ASCII para evitar problemas de encoding.

function Find-DotNetSdk10 {
    param([string]$PastaLocal)
    $candidatos = New-Object System.Collections.Generic.List[string]
    $comando = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if ($comando) { $candidatos.Add($comando.Source) }
    if ($env:ProgramFiles) { $candidatos.Add((Join-Path $env:ProgramFiles 'dotnet\dotnet.exe')) }
    $candidatos.Add((Join-Path $PastaLocal 'dotnet.exe'))

    foreach ($caminho in ($candidatos | Select-Object -Unique)) {
        if (-not [System.IO.File]::Exists($caminho)) { continue }
        try { $sdks = & $caminho --list-sdks 2>$null } catch { continue }
        if ($LASTEXITCODE -eq 0 -and (@($sdks) | Where-Object { $_ -match '^10\.' })) { return $caminho }
    }
    return $null
}

# Pastas do usuario sempre pelo caminho completo: com acento no nome do usuario (ex.: "Usuario" com acento),
# o Windows entrega o TEMP abreviado (C:\Users\USURIO~2\...) e o PowerShell 5.1 nao consegue usar esse caminho.
function Get-PastaLocalLonga {
    $local = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($local)) { $local = $env:LOCALAPPDATA }
    return $local
}

function Get-DotNet10 {
    $local = Get-PastaLocalLonga
    $pastaLocal = Join-Path $local 'Microsoft\dotnet'

    # TEMP com o caminho completo para esta janela (vale para o instalador e para a compilacao).
    $temp = Join-Path $local 'Temp'
    [System.IO.Directory]::CreateDirectory($temp) | Out-Null
    $env:TEMP = $temp
    $env:TMP = $temp

    $dotnet = Find-DotNetSdk10 -PastaLocal $pastaLocal

    if (-not $dotnet) {
        Write-Host ''
        Write-Host 'O SDK .NET 10 nao esta neste computador.' -ForegroundColor Yellow
        Write-Host 'Baixando e instalando da Microsoft (so na primeira vez, cerca de 250 MB; precisa de internet)...' -ForegroundColor Yellow

        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        [System.IO.Directory]::CreateDirectory($pastaLocal) | Out-Null
        $pastaInstalador = Join-Path $local 'ErpNaoFiscal\instalar-dotnet'
        [System.IO.Directory]::CreateDirectory($pastaInstalador) | Out-Null
        $instalador = Join-Path $pastaInstalador 'dotnet-install.ps1'
        try {
            try {
                # WebClient (do .NET): nao passa pelos caminhos do PowerShell, que falham com nomes abreviados.
                $web = New-Object System.Net.WebClient
                try { $web.DownloadFile('https://dot.net/v1/dotnet-install.ps1', $instalador) }
                finally { $web.Dispose() }
            }
            catch {
                throw ('Nao consegui baixar o instalador do .NET (sem internet ou bloqueado): ' + $_.Exception.Message +
                       ' Instale o SDK .NET 10 manualmente: https://dotnet.microsoft.com/download/dotnet/10.0')
            }
            # Em outro processo: o instalador da Microsoft nao foi feito para rodar com StrictMode.
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $instalador -Channel 10.0 -InstallDir $pastaLocal -NoPath
            if ($LASTEXITCODE -ne 0) {
                throw ('A instalacao do SDK .NET 10 falhou (codigo ' + $LASTEXITCODE + '). ' +
                       'Instale manualmente: https://dotnet.microsoft.com/download/dotnet/10.0')
            }
        }
        finally {
            try { if ([System.IO.File]::Exists($instalador)) { [System.IO.File]::Delete($instalador) } } catch { }
        }

        $dotnet = Find-DotNetSdk10 -PastaLocal $pastaLocal
        if (-not $dotnet) { throw 'O SDK .NET 10 foi instalado, mas nao foi encontrado depois. Feche e abra de novo e tente outra vez.' }
        Write-Host ('SDK .NET 10 instalado em ' + $pastaLocal) -ForegroundColor Green
    }

    # O dotnet escolhido vale para esta janela (inclusive para os processos que ele abre ao compilar).
    $pasta = Split-Path -Parent $dotnet
    $env:DOTNET_ROOT = $pasta
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if (-not (($env:PATH -split ';') -contains $pasta)) { $env:PATH = $pasta + ';' + $env:PATH }
    return $dotnet
}
