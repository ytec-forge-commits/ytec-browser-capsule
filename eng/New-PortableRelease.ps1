[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '1.2.1',

    [Parameter()]
    [string] $ArtifactsRoot = (
        Join-Path (Split-Path -Path $PSScriptRoot -Parent) 'artifacts'
    ),

    [Parameter()]
    [switch] $ReplaceExistingGeneratedArtifacts
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (
    Split-Path -Path $PSScriptRoot -Parent
)).Path
$dotnetPath = 'C:\Program Files\dotnet\dotnet.exe'
$dotnetRoot = Split-Path -Path $dotnetPath -Parent
$resolvedArtifactsRoot = [IO.Path]::GetFullPath($ArtifactsRoot)
$releaseName = "YtecBrowserCapsule-$Version-win-x64"
$releaseDirectory = Join-Path $resolvedArtifactsRoot $releaseName
$zipPath = Join-Path $resolvedArtifactsRoot "$releaseName.zip"
$zipHashPath = "$zipPath.sha256"
$japaneseManualPath = Join-Path $resolvedArtifactsRoot (
    "YtecBrowserCapsule-$Version-User-Manual-ja.pdf"
)
$englishManualPath = Join-Path $resolvedArtifactsRoot (
    "YtecBrowserCapsule-$Version-User-Manual-en.pdf"
)

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw ".NET実行ファイルが見つかりません: $dotnetPath"
}

foreach ($target in @(
    $releaseDirectory,
    $zipPath,
    $zipHashPath,
    $japaneseManualPath,
    $englishManualPath
)) {
    if (-not (Test-Path -LiteralPath $target)) {
        continue
    }

    if (-not $ReplaceExistingGeneratedArtifacts) {
        throw "既存のRelease成果物は上書きしません: $target"
    }

    $resolvedTarget = [IO.Path]::GetFullPath($target)
    $artifactsPrefix = $resolvedArtifactsRoot +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTarget.StartsWith(
        $artifactsPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Release成果物の削除対象がartifacts外です: $resolvedTarget"
    }

    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedArtifactsRoot -Force |
    Out-Null

& $dotnetPath publish (
    Join-Path $repositoryRoot (
        'src\Ytec.BrowserCapsule.App\Ytec.BrowserCapsule.App.csproj'
    )
) `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $releaseDirectory

if ($LASTEXITCODE -ne 0) {
    throw '自己完結型Releaseのpublishに失敗しました。'
}

$copies = [ordered]@{
    (Join-Path $repositoryRoot 'LICENSE') = 'LICENSE.txt'
    (Join-Path $repositoryRoot 'NOTICE') = 'NOTICE.txt'
    (Join-Path $repositoryRoot 'README.md') = 'README.md'
    (Join-Path $repositoryRoot 'README.en.md') = 'README.en.md'
    (Join-Path $repositoryRoot 'PRIVACY.md') = 'PRIVACY.md'
    (Join-Path $repositoryRoot 'SECURITY.md') = 'SECURITY.md'
    (Join-Path $repositoryRoot 'CODE_SIGNING.md') = 'CODE_SIGNING.md'
    (Join-Path $repositoryRoot 'CHANGELOG.md') = 'CHANGELOG.md'
    (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') =
        'THIRD-PARTY-NOTICES.md'
    (Join-Path $repositoryRoot (
        'output\pdf\' +
        "Y-TEC_Browser_Capsule_操作マニュアル_$Version.pdf"
    )) = '操作マニュアル.pdf'
    (Join-Path $repositoryRoot (
        'output\pdf\' +
        "Y-TEC_Browser_Capsule_User_Manual_$Version.pdf"
    )) = 'Operation Manual.pdf'
    (Join-Path $repositoryRoot 'docs\threat-model.md') =
        'セキュリティについて.md'
    (Join-Path $repositoryRoot 'docs\error-codes.md') =
        'エラーコード.md'
    (Join-Path $dotnetRoot 'LICENSE.txt') = 'DOTNET-LICENSE.txt'
    (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') =
        'DOTNET-THIRD-PARTY-NOTICES.txt'
}

foreach ($copy in $copies.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $copy.Key -PathType Leaf)) {
        throw "同梱文書が見つかりません: $($copy.Key)"
    }

    Copy-Item -LiteralPath $copy.Key -Destination (
        Join-Path $releaseDirectory $copy.Value
    )
}

& (Join-Path $PSScriptRoot 'Write-ReleaseHashes.ps1') `
    -ArtifactDirectory $releaseDirectory `
    -OutputFile (Join-Path $releaseDirectory 'SHA256SUMS.txt')

Compress-Archive `
    -Path (Join-Path $releaseDirectory '*') `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

$zipHash = (
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $zipHashPath,
    "$zipHash *$releaseName.zip`n",
    [Text.UTF8Encoding]::new($false))

Copy-Item -LiteralPath (Join-Path $releaseDirectory '操作マニュアル.pdf') `
    -Destination $japaneseManualPath
Copy-Item -LiteralPath (Join-Path $releaseDirectory 'Operation Manual.pdf') `
    -Destination $englishManualPath

& (Join-Path $PSScriptRoot 'Test-PortableRelease.ps1') `
    -Version $Version `
    -ArtifactsRoot $resolvedArtifactsRoot

Write-Host "ポータブルReleaseを作成しました: $zipPath"
