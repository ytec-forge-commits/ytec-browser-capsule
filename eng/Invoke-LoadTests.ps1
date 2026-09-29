[CmdletBinding()]
param(
    [ValidateSet('all', 'large-file', 'many-files')]
    [string] $Scenario = 'all',

    [string] $WorkRoot = (
        Join-Path $PSScriptRoot '..\.validation\YtecBrowserCapsule-LoadTests'
    ),

    [string] $ResultPath = (
        Join-Path $PSScriptRoot '..\.validation\evidence\load-tests.json'
    ),

    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$project = Join-Path (
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
) 'tests\Ytec.BrowserCapsule.LoadTests\Ytec.BrowserCapsule.LoadTests.csproj'
$workRootPath = [IO.Path]::GetFullPath($WorkRoot)
$resultPathValue = [IO.Path]::GetFullPath($ResultPath)

if (
    [IO.Path]::GetFileName($workRootPath) -cne
        'YtecBrowserCapsule-LoadTests'
) {
    throw (
        '負荷試験の作業ルート末尾は' +
        'YtecBrowserCapsule-LoadTestsにしてください。'
    )
}

$requiredBytes = if ($Scenario -eq 'many-files') {
    1GB
}
else {
    12GB
}
$driveRoot = [IO.Path]::GetPathRoot($workRootPath)
$drive = [IO.DriveInfo]::new($driveRoot)
if (-not $drive.IsReady -or $drive.AvailableFreeSpace -lt $requiredBytes) {
    throw (
        '負荷試験に必要な空き容量がありません。' +
        " required=$requiredBytes available=$($drive.AvailableFreeSpace)"
    )
}

$arguments = @(
    'run',
    '--configuration',
    'Release',
    '--project',
    $project,
    '--',
    '--work-root',
    $workRootPath,
    '--result',
    $resultPathValue,
    '--scenario',
    $Scenario
)
if ($KeepArtifacts) {
    $arguments += '--keep'
}

& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "負荷試験が失敗しました。exit=$LASTEXITCODE"
}

Get-Content -LiteralPath $resultPathValue -Raw
