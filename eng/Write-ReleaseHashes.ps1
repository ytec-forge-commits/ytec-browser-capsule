[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [Parameter(Mandatory)]
    [string] $OutputFile
)

$ErrorActionPreference = 'Stop'

$resolvedArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$resolvedOutputFile = [IO.Path]::GetFullPath($OutputFile)
$outputParent = Split-Path -Path $resolvedOutputFile -Parent

if (-not (Test-Path -LiteralPath $outputParent)) {
    New-Item -ItemType Directory -Path $outputParent | Out-Null
}

$hashLines = Get-ChildItem -LiteralPath $resolvedArtifactDirectory -File -Recurse |
    Where-Object { $_.FullName -ne $resolvedOutputFile } |
    Sort-Object FullName |
    ForEach-Object {
        $relativePath = [IO.Path]::GetRelativePath(
            $resolvedArtifactDirectory,
            $_.FullName).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$relativePath"
    }

[IO.File]::WriteAllLines(
    $resolvedOutputFile,
    $hashLines,
    [Text.UTF8Encoding]::new($false))

Write-Host "SHA-256一覧を作成しました: $resolvedOutputFile ($($hashLines.Count) ファイル)"
