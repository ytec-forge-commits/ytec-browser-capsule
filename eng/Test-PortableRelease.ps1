[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $ArtifactsRoot = (Join-Path $PSScriptRoot '..\artifacts'),

    [switch] $RequireSignature
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$releaseName = "YtecBrowserCapsule-$Version-win-x64"
$artifactsRootPath = [IO.Path]::GetFullPath($ArtifactsRoot)
$releaseDirectory = Join-Path $artifactsRootPath $releaseName
$zipPath = Join-Path $artifactsRootPath "$releaseName.zip"
$zipHashPath = "$zipPath.sha256"
$manifestPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$executablePath = Join-Path $releaseDirectory 'YtecBrowserCapsule.exe'
$japaneseManualPath = Join-Path $artifactsRootPath (
    "YtecBrowserCapsule-$Version-User-Manual-ja.pdf"
)
$englishManualPath = Join-Path $artifactsRootPath (
    "YtecBrowserCapsule-$Version-User-Manual-en.pdf"
)

foreach ($requiredPath in @(
    $releaseDirectory,
    $zipPath,
    $zipHashPath,
    $manifestPath,
    $executablePath,
    $japaneseManualPath,
    $englishManualPath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Release検証に必要な成果物がありません: $requiredPath"
    }
}

foreach ($manualPair in @(
    [PSCustomObject]@{
        Packaged = Join-Path $releaseDirectory '操作マニュアル.pdf'
        ReleaseAsset = $japaneseManualPath
    },
    [PSCustomObject]@{
        Packaged = Join-Path $releaseDirectory 'Operation Manual.pdf'
        ReleaseAsset = $englishManualPath
    }
)) {
    $packagedHash = (
        Get-FileHash -LiteralPath $manualPair.Packaged -Algorithm SHA256
    ).Hash
    $releaseAssetHash = (
        Get-FileHash -LiteralPath $manualPair.ReleaseAsset -Algorithm SHA256
    ).Hash
    if ($packagedHash -ne $releaseAssetHash) {
        throw "Release用PDFとZIP同梱PDFのSHA-256が一致しません。"
    }
}

$manifestEntries = [Collections.Generic.Dictionary[string, string]]::new(
    [StringComparer]::Ordinal)
foreach ($line in [IO.File]::ReadAllLines(
    $manifestPath,
    [Text.Encoding]::UTF8
)) {
    if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') {
        throw "SHA256SUMS.txtに不正な行があります。"
    }

    $relativePath = $Matches[2].Replace('\', '/')
    if (-not $manifestEntries.TryAdd($relativePath, $Matches[1])) {
        throw "SHA256SUMS.txtに重複パスがあります: $relativePath"
    }
}

if ($manifestEntries.Count -eq 0) {
    throw 'SHA256SUMS.txtが空です。'
}

foreach ($entry in $manifestEntries.GetEnumerator()) {
    $filePath = Join-Path (
        $releaseDirectory
    ) $entry.Key.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Releaseフォルダー内のファイルがありません: $($entry.Key)"
    }

    $actualHash = (
        Get-FileHash -LiteralPath $filePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($actualHash -ne $entry.Value) {
        throw "Releaseフォルダー内のハッシュが一致しません: $($entry.Key)"
    }
}

$actualReleaseFiles = @(
    Get-ChildItem -LiteralPath $releaseDirectory -File -Recurse |
        Where-Object { $_.FullName -ne $manifestPath }
)
if ($actualReleaseFiles.Count -ne $manifestEntries.Count) {
    throw (
        'Releaseフォルダー内のファイル数が一致しません。' +
        " expected=$($manifestEntries.Count)" +
        " actual=$($actualReleaseFiles.Count)"
    )
}

$zipHash = (
    Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
).Hash.ToLowerInvariant()
$sidecar = [IO.File]::ReadAllText(
    $zipHashPath,
    [Text.Encoding]::UTF8).Trim()
$escapedReleaseName = [Regex]::Escape($releaseName)
if ($sidecar -notmatch (
    "^([0-9a-f]{64}) \*$escapedReleaseName\.zip$"
)) {
    throw 'ZIPのSHA-256 sidecar形式が不正です。'
}

if ($Matches[1] -ne $zipHash) {
    throw 'ZIPのSHA-256がsidecarと一致しません。'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $zipEntries = [Collections.Generic.Dictionary[
        string, IO.Compression.ZipArchiveEntry
    ]]::new([StringComparer]::Ordinal)
    foreach ($entry in $archive.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) {
            continue
        }

        $entryName = $entry.FullName.Replace('\', '/')
        if (-not $zipEntries.TryAdd($entryName, $entry)) {
            throw "ZIPに重複パスがあります: $entryName"
        }
    }

    foreach ($manifestEntry in $manifestEntries.GetEnumerator()) {
        $zipEntry = $null
        if (-not $zipEntries.TryGetValue(
            $manifestEntry.Key,
            [ref] $zipEntry
        )) {
            throw "ZIP内のファイルがありません: $($manifestEntry.Key)"
        }

        $stream = $zipEntry.Open()
        try {
            $sha256 = [Security.Cryptography.SHA256]::Create()
            try {
                $actualHash = [Convert]::ToHexString(
                    $sha256.ComputeHash($stream)
                ).ToLowerInvariant()
            }
            finally {
                $sha256.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        if ($actualHash -ne $manifestEntry.Value) {
            throw "ZIP内のハッシュが一致しません: $($manifestEntry.Key)"
        }
    }

    $requiredFiles = @(
        'YtecBrowserCapsule.exe',
        'LICENSE.txt',
        'NOTICE.txt',
        'README.md',
        'README.en.md',
        'PRIVACY.md',
        'SECURITY.md',
        'CODE_SIGNING.md',
        'CHANGELOG.md',
        'THIRD-PARTY-NOTICES.md',
        'DOTNET-LICENSE.txt',
        'DOTNET-THIRD-PARTY-NOTICES.txt',
        '操作マニュアル.pdf',
        'Operation Manual.pdf',
        'セキュリティについて.md',
        'エラーコード.md',
        'SHA256SUMS.txt'
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not $zipEntries.ContainsKey($requiredFile)) {
            throw "ZIP内に必須ファイルがありません: $requiredFile"
        }
    }

    if ($zipEntries.Count -ne ($manifestEntries.Count + 1)) {
        throw (
            'ZIP内のファイル数が一致しません。' +
            " expected=$($manifestEntries.Count + 1)" +
            " actual=$($zipEntries.Count)"
        )
    }

    $pdbEntries = @(
        $zipEntries.Keys |
            Where-Object { $_.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase) }
    )
    if ($pdbEntries.Count -ne 0) {
        throw "配布ZIPにPDBが含まれています: $($pdbEntries -join ', ')"
    }

    $forbiddenSecretExtensions = @(
        '.bvb', '.csv', '.key', '.p12', '.pem', '.pfx', '.snk', '.ybckey'
    )
    $secretEntries = @(
        $zipEntries.Keys |
            Where-Object {
                $forbiddenSecretExtensions -contains
                    [IO.Path]::GetExtension($_).ToLowerInvariant()
            }
    )
    if ($secretEntries.Count -ne 0) {
        throw (
            '配布ZIPに秘密情報を含み得る禁止拡張子があります: ' +
            ($secretEntries -join ', ')
        )
    }

    $forbiddenSecretNames = @(
        '.env', 'credentials.json', 'secrets.json'
    )
    $secretNameEntries = @(
        $zipEntries.Keys |
            Where-Object {
                $forbiddenSecretNames -contains
                    [IO.Path]::GetFileName($_).ToLowerInvariant()
            }
    )
    if ($secretNameEntries.Count -ne 0) {
        throw (
            '配布ZIPに秘密情報を含み得る禁止ファイルがあります: ' +
            ($secretNameEntries -join ', ')
        )
    }

    $releaseSecretRules = [ordered]@{
        PrivateKey = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
        AwsAccessKey = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
        GitHubToken = '\bgh(?:p|o|u|s|r)_[A-Za-z0-9]{30,}\b'
        SlackToken = '\bxox(?:b|p|a|r|s)-[A-Za-z0-9-]{20,}\b'
        GoogleApiKey = '\bAIza[0-9A-Za-z_-]{35}\b'
        AzureStorageConnectionString =
            '(?i)AccountKey\s*=\s*[A-Za-z0-9+/]{40,}={0,2}'
    }
    $releaseTextExtensions = @(
        '.config', '.json', '.md', '.txt', '.xml', '.yaml', '.yml'
    )
    $releaseSecretFindings = [Collections.Generic.List[string]]::new()
    foreach ($entry in $archive.Entries) {
        $extension = [IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
        if ($releaseTextExtensions -notcontains $extension -or $entry.Length -gt 2MB) {
            continue
        }

        $entryStream = $entry.Open()
        try {
            $reader = [IO.StreamReader]::new(
                $entryStream,
                [Text.Encoding]::UTF8,
                $true,
                4096,
                $true
            )
            try {
                $content = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $entryStream.Dispose()
        }

        foreach ($rule in $releaseSecretRules.GetEnumerator()) {
            if ([Text.RegularExpressions.Regex]::IsMatch(
                $content,
                $rule.Value
            )) {
                $releaseSecretFindings.Add(
                    "$($entry.FullName) [$($rule.Key)]"
                )
            }
        }
    }
    if ($releaseSecretFindings.Count -ne 0) {
        throw (
            '配布ZIPに秘密情報の可能性があるパターンを検出しました。' +
            '値は表示しません。' + [Environment]::NewLine +
            ($releaseSecretFindings -join [Environment]::NewLine)
        )
    }
}
finally {
    $archive.Dispose()
}

$fileVersionText = (
    [Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath)
).FileVersion
$fileVersion = [Version]::Parse($fileVersionText)
$expectedVersion = [Version]::Parse($Version)
if (
    $fileVersion.Major -ne $expectedVersion.Major -or
    $fileVersion.Minor -ne $expectedVersion.Minor -or
    $fileVersion.Build -ne $expectedVersion.Build
) {
    throw (
        "EXEのFileVersionが一致しません。" +
        " expected=$Version actual=$fileVersionText"
    )
}

$signature = Get-AuthenticodeSignature -LiteralPath $executablePath
if ($RequireSignature -and $signature.Status -ne 'Valid') {
    throw "Authenticode署名が有効ではありません: $($signature.Status)"
}

if ($signature.Status -ne 'Valid') {
    Write-Warning (
        "Authenticode署名はありません: $($signature.Status)。" +
        '未署名配布ではSHA-256一覧とZIP sidecarを必ず公開してください。'
    )
}

[PSCustomObject]@{
    ReleaseName = $releaseName
    HashedFiles = $manifestEntries.Count
    ZipEntries = $zipEntries.Count
    ZipSha256 = $zipHash
    FileVersion = $fileVersionText
    SignatureStatus = $signature.Status
    RequiredFiles = 'OK'
    PdbCount = 0
} | Format-List
