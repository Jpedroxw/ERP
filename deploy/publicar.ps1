# Projeto JP - publicador dos quatro componentes (PowerShell 5.1 e 7).
# Executar a partir da pasta da solucao:
#   powershell.exe -NoExit -ExecutionPolicy Bypass -File .\deploy\publicar.ps1
# Este arquivo usa somente caracteres ASCII para evitar problemas de encoding.
# Nao modifica a VPS, o PostgreSQL nem os bancos locais dos caixas.
# Se faltar o SDK .NET 10, baixa e instala sozinho (garantir-dotnet.ps1). Os pacotes NuGet vem no proprio publish.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$raiz = Split-Path -Parent $PSScriptRoot
$saida = Join-Path $raiz 'publicar'
$temporaria = Join-Path $raiz ('publicar_em_progresso_' + [Guid]::NewGuid().ToString('N'))
$anterior = Join-Path $raiz ('publicar_anterior_' + [Guid]::NewGuid().ToString('N'))
$moveuAnterior = $false
# Versao gravada nos programas (ano.mes.dia.horaminuto): a atualizacao automatica compara com a do servidor.
$versao = (Get-Date).ToString('yyyy.M.d.HHmm')

$projetos = @(
    # A API continua gerando o arquivo Erp.Api: e o nome que o servico erp-nf-api executa na VPS.
    @{ Nome='API Linux'; Pasta='api'; Caminho='src\ProjetoJP.Api\ProjetoJP.Api.csproj'; RID='linux-x64'; Exe='Erp.Api'; Unico=$false },
    @{ Nome='PDV Windows'; Pasta='pdv'; Caminho='src\ProjetoJP.Pdv\ProjetoJP.Pdv.csproj'; RID='win-x64'; Exe='ProjetoJP-PDV.exe'; Unico=$true },
    @{ Nome='Gestao Windows'; Pasta='gestao'; Caminho='src\ProjetoJP.Gestao\ProjetoJP.Gestao.csproj'; RID='win-x64'; Exe='ProjetoJP-Gestao.exe'; Unico=$true },
    @{ Nome='Master Windows'; Pasta='master'; Caminho='src\ProjetoJP.Master\ProjetoJP.Master.csproj'; RID='win-x64'; Exe='ProjetoJP-Master.exe'; Unico=$true },
    # Windows 10 de 32 bits (PCs antigos/simples): mesma coisa, compilada para x86.
    @{ Nome='PDV Windows 32 bits'; Pasta='pdv-32bits'; Caminho='src\ProjetoJP.Pdv\ProjetoJP.Pdv.csproj'; RID='win-x86'; Exe='ProjetoJP-PDV.exe'; Unico=$true },
    @{ Nome='Gestao Windows 32 bits'; Pasta='gestao-32bits'; Caminho='src\ProjetoJP.Gestao\ProjetoJP.Gestao.csproj'; RID='win-x86'; Exe='ProjetoJP-Gestao.exe'; Unico=$true }
)

try {
    # Sem o SDK .NET 10 no computador: baixa e instala sozinho (so na primeira vez).
    . (Join-Path $PSScriptRoot 'garantir-dotnet.ps1')
    $dotnet = Get-DotNet10

    $sdk = & $dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel identificar o SDK do .NET.' }
    Write-Host ('SDK detectado: ' + $sdk) -ForegroundColor Gray
    Write-Host ('Versao desta publicacao: ' + $versao) -ForegroundColor Gray

    foreach ($projeto in $projetos) {
        $arquivo = Join-Path $raiz $projeto.Caminho
        if (-not (Test-Path -LiteralPath $arquivo -PathType Leaf)) {
            throw ('Projeto nao encontrado: ' + $arquivo)
        }
    }

    New-Item -ItemType Directory -Path $temporaria -Force | Out-Null

    foreach ($projeto in $projetos) {
        $arquivo = Join-Path $raiz $projeto.Caminho
        $pasta = Join-Path $temporaria $projeto.Pasta
        Write-Host ('==> Publicando ' + $projeto.Nome) -ForegroundColor Cyan

        $argumentos = @('publish', $arquivo, '-c', 'Release', '-r', $projeto.RID,
                        '--self-contained', 'true', '-o', $pasta, ('-p:Version=' + $versao))
        if ($projeto.Unico) {
            $argumentos += '-p:PublishSingleFile=true'
            $argumentos += '-p:IncludeNativeLibrariesForSelfExtract=true'
            # Incorpora simbolos de depuracao em vez de gerar .pdb separado.
            $argumentos += '-p:DebugType=embedded'
        }

        & $dotnet @argumentos
        if ($LASTEXITCODE -ne 0) {
            throw ('Falha ao publicar ' + $projeto.Nome + '. Codigo: ' + $LASTEXITCODE)
        }
        $executavel = Join-Path $pasta $projeto.Exe
        if (-not (Test-Path -LiteralPath $executavel -PathType Leaf)) {
            throw ('A publicacao terminou sem o executavel esperado: ' + $executavel)
        }
        Write-Host ('OK: ' + $executavel) -ForegroundColor Green
    }

    # Instalador (PDV e Gestao, 64 e 32 bits) com o visual do sistema: publicar\instalador\Instalar-ProjetoJP.exe.
    # O instalador e de 32 bits (roda nos dois Windows) e instala a versao certa para o Windows de cada computador.
    Write-Host '==> Gerando o instalador (PDV e Gestao)' -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $pacote = Join-Path $temporaria 'pacote-programas.zip'
    $conteudo = @(
        @{ Origem = 'pdv\ProjetoJP-PDV.exe';              Nome = 'caixa/x64/ProjetoJP-PDV.exe' },
        @{ Origem = 'pdv-32bits\ProjetoJP-PDV.exe';       Nome = 'caixa/x86/ProjetoJP-PDV.exe' },
        @{ Origem = 'gestao\ProjetoJP-Gestao.exe';        Nome = 'retaguarda/x64/ProjetoJP-Gestao.exe' },
        @{ Origem = 'gestao-32bits\ProjetoJP-Gestao.exe'; Nome = 'retaguarda/x86/ProjetoJP-Gestao.exe' }
    )
    $zip = [System.IO.Compression.ZipFile]::Open($pacote, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $conteudo) {
            $arquivoOrigem = Join-Path $temporaria $item.Origem
            if (-not (Test-Path -LiteralPath $arquivoOrigem -PathType Leaf)) { throw ('Falta para o instalador: ' + $arquivoOrigem) }
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $arquivoOrigem, $item.Nome,
                [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $zip.Dispose() }

    $pastaInstalador = Join-Path $temporaria 'instalador'
    $projetoInstalador = Join-Path $raiz 'src\ProjetoJP.Instalador\ProjetoJP.Instalador.csproj'
    & $dotnet publish $projetoInstalador -c Release -r win-x86 --self-contained true -o $pastaInstalador `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded "-p:PacoteProgramas=$pacote" "-p:Version=$versao"
    if ($LASTEXITCODE -ne 0) { throw ('Falha ao gerar o instalador. Codigo: ' + $LASTEXITCODE) }
    Remove-Item -LiteralPath $pacote -Force
    $exeInstalador = Join-Path $pastaInstalador 'Instalar-ProjetoJP.exe'
    if (-not (Test-Path -LiteralPath $exeInstalador -PathType Leaf)) { throw ('O instalador nao foi gerado: ' + $exeInstalador) }
    Write-Host ('OK: ' + $exeInstalador) -ForegroundColor Green

    # Atualizacao automatica: um pacote por programa e arquitetura + versao.json (tamanho e SHA-256 de cada um).
    # A pasta publicar\atualizacao vai inteira para /opt/erp-nf/downloads/atualizacao na VPS.
    # Dentro do zip o programa vai com o nome antigo (ErpPdv.exe / ErpRetaguarda.exe): e o que as versoes de antes do
    # Projeto JP procuram para se atualizar. As versoes novas aceitam qualquer nome (o unico .exe do pacote).
    Write-Host '==> Gerando os pacotes da atualizacao automatica' -ForegroundColor Cyan
    $pastaAtualizacao = Join-Path $temporaria 'atualizacao'
    New-Item -ItemType Directory -Path $pastaAtualizacao -Force | Out-Null
    $pacotes = @(
        @{ Chave = 'caixa-x64';      Origem = 'pdv\ProjetoJP-PDV.exe';              Exe = 'ErpPdv.exe' },
        @{ Chave = 'caixa-x86';      Origem = 'pdv-32bits\ProjetoJP-PDV.exe';       Exe = 'ErpPdv.exe' },
        @{ Chave = 'retaguarda-x64'; Origem = 'gestao\ProjetoJP-Gestao.exe';        Exe = 'ErpRetaguarda.exe' },
        @{ Chave = 'retaguarda-x86'; Origem = 'gestao-32bits\ProjetoJP-Gestao.exe'; Exe = 'ErpRetaguarda.exe' }
    )
    $arquivosManifesto = [ordered]@{}
    foreach ($item in $pacotes) {
        $nomeZip = $item.Chave + '.zip'
        $caminhoZip = Join-Path $pastaAtualizacao $nomeZip
        $zipAtualizacao = [System.IO.Compression.ZipFile]::Open($caminhoZip, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipAtualizacao, (Join-Path $temporaria $item.Origem),
                $item.Exe, [System.IO.Compression.CompressionLevel]::Optimal)
        }
        finally { $zipAtualizacao.Dispose() }
        $arquivosManifesto[$item.Chave] = [ordered]@{
            arquivo = $nomeZip
            tamanho = (Get-Item -LiteralPath $caminhoZip).Length
            sha256  = (Get-FileHash -LiteralPath $caminhoZip -Algorithm SHA256).Hash
        }
    }
    $manifesto = [ordered]@{ versao = $versao; geradoEm = (Get-Date).ToString('s'); arquivos = $arquivosManifesto }
    [System.IO.File]::WriteAllText((Join-Path $pastaAtualizacao 'versao.json'), ($manifesto | ConvertTo-Json -Depth 5),
        (New-Object System.Text.UTF8Encoding $false))
    Write-Host ('OK: ' + $pastaAtualizacao + ' (versao ' + $versao + ')') -ForegroundColor Green

    # So troca a publicacao anterior quando os quatro componentes estao prontos.
    if (Test-Path -LiteralPath $saida) {
        Move-Item -LiteralPath $saida -Destination $anterior
        $moveuAnterior = $true
    }
    Move-Item -LiteralPath $temporaria -Destination $saida
    if ($moveuAnterior -and (Test-Path -LiteralPath $anterior)) {
        Remove-Item -LiteralPath $anterior -Recurse -Force
        $moveuAnterior = $false
    }

    Write-Host ''
    Write-Host ('PUBLICACAO CONCLUIDA: ' + $saida) -ForegroundColor Green
    Write-Host 'API: publicar\api -> VPS Debian (/opt/erp-nf/api)'
    Write-Host 'PDV: publicar\pdv -> computadores dos caixas'
    Write-Host 'Gestao: publicar\gestao -> computador da loja'
    Write-Host 'Master: publicar\master -> computador administrativo'
    Write-Host 'Windows 32 bits: publicar\pdv-32bits e publicar\gestao-32bits (veja em Configuracoes > Sistema > Sobre)'
    Write-Host 'Instalador: publicar\instalador\Instalar-ProjetoJP.exe -> PDV e Gestao nas lojas (escolhe 64/32 bits sozinho)'
    Write-Host ('Atualizacao automatica: publicar\atualizacao -> VPS em /opt/erp-nf/downloads/atualizacao (versao ' + $versao + ')')
    Write-Host 'Os tres aplicativos Windows sao publicados como EXE unico por modulo.'
    Write-Host 'Se houver arquivos adicionais na pasta, confira-os antes de excluir.'
    Write-Host ''
    Write-Host 'ATENCAO: nao altere /etc/erp-nf/api.env nem apague os bancos.' -ForegroundColor Yellow
    Write-Host 'Valide a API antes de atualizar o servico erp-nf-api.' -ForegroundColor Yellow
}
catch {
    if ($moveuAnterior -and -not (Test-Path -LiteralPath $saida) -and
        (Test-Path -LiteralPath $anterior)) {
        Move-Item -LiteralPath $anterior -Destination $saida
        $moveuAnterior = $false
    }
    Write-Host ''
    Write-Host ('PUBLICACAO FALHOU: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'A publicacao anterior foi preservada, se existia.' -ForegroundColor Yellow
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaria) {
        Remove-Item -LiteralPath $temporaria -Recurse -Force
    }
}
