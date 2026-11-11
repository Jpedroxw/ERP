param([string]$Destino = (Join-Path $PSScriptRoot '../../ProjetoJP-fonte.zip'))
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$temporario = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item -ItemType Directory $temporario | Out-Null
# Lista permitida: não inclui instaladores, binários, bancos ou configurações locais.
Get-ChildItem $raiz -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs|publicar)[\\/]' -and
    $_.Extension -in '.cs','.csproj','.slnx','.props','.targets','.md','.html','.css','.js','.sh','.ps1','.cmd','.conf' -and
    $_.Name -notmatch 'appsettings|\.env'
} | ForEach-Object {
    $rel = [IO.Path]::GetRelativePath($raiz, $_.FullName)
    $alvo = Join-Path $temporario $rel
    New-Item -ItemType Directory -Force (Split-Path $alvo) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $alvo
}
Compress-Archive -Path (Join-Path $temporario '*') -DestinationPath $Destino -Force
Write-Host "Código exportado: $Destino"
Write-Host "Configurações e recursos de instalação devem ser fornecidos separadamente."
