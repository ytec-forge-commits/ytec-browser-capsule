[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $CertificateThumbprint,

    [Parameter()]
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string] $CertificateStoreLocation = 'CurrentUser',

    [Parameter()]
    [ValidatePattern('^https://')]
    [string] $TimestampServer = 'https://timestamp.digicert.com',

    [Parameter()]
    [string] $ArtifactsRoot = (
        Join-Path (Split-Path -Path $PSScriptRoot -Parent) 'artifacts'
    ),

    [Parameter()]
    [switch] $ReplaceExistingGeneratedPackage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path -LiteralPath (
    Split-Path -Path $PSScriptRoot -Parent
)).Path
$resolvedArtifactsRoot = [IO.Path]::GetFullPath($ArtifactsRoot)
$releaseName = "YtecBrowserCapsule-$Version-win-x64"
$releaseDirectory = Join-Path $resolvedArtifactsRoot $releaseName
$zipPath = Join-Path $resolvedArtifactsRoot "$releaseName.zip"
$zipHashPath = "$zipPath.sha256"
$manifestPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
$executablePath = Join-Path $releaseDirectory 'YtecBrowserCapsule.exe'
$normalizedThumbprint = $CertificateThumbprint.ToUpperInvariant()

foreach ($requiredPath in @($releaseDirectory, $executablePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "署名対象のRelease成果物がありません: $requiredPath"
    }
}

$releasePath = (Resolve-Path -LiteralPath $releaseDirectory).Path
$artifactsPrefix = $resolvedArtifactsRoot +
    [IO.Path]::DirectorySeparatorChar
if (-not $releasePath.StartsWith(
    $artifactsPrefix,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "署名対象がartifacts外です: $releasePath"
}

$existingPackages = @(
    $zipPath,
    $zipHashPath
) | Where-Object { Test-Path -LiteralPath $_ }
if (
    $existingPackages.Count -gt 0 -and
    -not $ReplaceExistingGeneratedPackage
) {
    throw (
        '既存の生成済みZIPを署名後の内容で置き換えます。' +
        '意図した操作なら-ReplaceExistingGeneratedPackageを指定してください。'
    )
}

$certificatePath = (
    "Cert:\$CertificateStoreLocation\My\$normalizedThumbprint"
)
if (-not (Test-Path -LiteralPath $certificatePath)) {
    throw "コード署名証明書が見つかりません: $certificatePath"
}

$certificate = Get-Item -LiteralPath $certificatePath
$now = Get-Date
if (-not $certificate.HasPrivateKey) {
    throw 'コード署名証明書の秘密鍵を利用できません。'
}

if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
    throw (
        'コード署名証明書が有効期間外です。' +
        " NotBefore=$($certificate.NotBefore.ToString('O'))" +
        " NotAfter=$($certificate.NotAfter.ToString('O'))"
    )
}

$codeSigningOid = '1.3.6.1.5.5.7.3.3'
$hasCodeSigningEku = $certificate.Extensions |
    Where-Object {
        $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]
    } |
    ForEach-Object { $_.EnhancedKeyUsages } |
    Where-Object { $_.Value -eq $codeSigningOid }
if ($null -eq $hasCodeSigningEku) {
    throw '証明書にコード署名EKUがありません。'
}

$windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signToolPath = Get-ChildItem -LiteralPath $windowsKitsRoot -Directory |
    Sort-Object {
        $parsedVersion = [Version]::new()
        if ([Version]::TryParse($_.Name, [ref] $parsedVersion)) {
            return $parsedVersion
        }

        return [Version]::new()
    } -Descending |
    ForEach-Object {
        Join-Path $_.FullName 'x64\signtool.exe'
    } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($signToolPath)) {
    throw 'Windows SDKのx64版signtool.exeが見つかりません。'
}

$signArguments = @(
    'sign',
    '/sha1',
    $normalizedThumbprint,
    '/s',
    'My',
    '/fd',
    'SHA256',
    '/tr',
    $TimestampServer,
    '/td',
    'SHA256',
    '/d',
    'Y-TEC Browser Capsule',
    '/v'
)
if ($CertificateStoreLocation -eq 'LocalMachine') {
    $signArguments += '/sm'
}

$signArguments += $executablePath
& $signToolPath @signArguments
if ($LASTEXITCODE -ne 0) {
    throw "Authenticode署名に失敗しました。exit=$LASTEXITCODE"
}

& $signToolPath verify /pa /all /v $executablePath
if ($LASTEXITCODE -ne 0) {
    throw "Authenticode署名の検証に失敗しました。exit=$LASTEXITCODE"
}

$signature = Get-AuthenticodeSignature -LiteralPath $executablePath
if ($signature.Status -ne 'Valid') {
    throw "Authenticode署名が有効ではありません: $($signature.Status)"
}

if (
    $null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Thumbprint -ne $normalizedThumbprint
) {
    throw '署名者証明書の拇印が指定値と一致しません。'
}

if ($null -eq $signature.TimeStamperCertificate) {
    throw 'RFC 3161タイムスタンプを確認できません。'
}

& (Join-Path $repositoryRoot 'eng\Write-ReleaseHashes.ps1') `
    -ArtifactDirectory $releaseDirectory `
    -OutputFile $manifestPath

$temporaryZipPath = Join-Path $resolvedArtifactsRoot (
    ".$releaseName.signing-$([Guid]::NewGuid().ToString('N')).zip"
)
try {
    Compress-Archive `
        -Path (Join-Path $releaseDirectory '*') `
        -DestinationPath $temporaryZipPath `
        -CompressionLevel Optimal

    Move-Item `
        -LiteralPath $temporaryZipPath `
        -Destination $zipPath `
        -Force

    $zipHash = (
        Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(
        $zipHashPath,
        "$zipHash *$releaseName.zip`n",
        [Text.UTF8Encoding]::new($false))

    & (Join-Path $repositoryRoot 'eng\Test-PortableRelease.ps1') `
        -Version $Version `
        -ArtifactsRoot $resolvedArtifactsRoot `
        -RequireSignature
}
finally {
    if (Test-Path -LiteralPath $temporaryZipPath) {
        Remove-Item -LiteralPath $temporaryZipPath -Force
    }
}

Write-Host "署名済みポータブルReleaseを再封印しました: $zipPath"
