[CmdletBinding()]
param(
    [Parameter()]
    [string] $RepositoryRoot = (Split-Path -Path $PSScriptRoot -Parent),

    [Parameter()]
    [switch] $IncludeGitHistory
)

$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$selfPath = $MyInvocation.MyCommand.Path
$excludedDirectoryNames = @(
    '.git',
    '.validation',
    'artifacts',
    'bin',
    'obj',
    'TestResults'
)
$textExtensions = @(
    '.cs',
    '.csproj',
    '.json',
    '.manifest',
    '.md',
    '.props',
    '.ps1',
    '.sln',
    '.targets',
    '.xaml',
    '.xml',
    '.yaml',
    '.yml'
)

$rules = [ordered]@{
    PrivateKey = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    AwsAccessKey = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
    GitHubToken = '\bgh(?:p|o|u|s|r)_[A-Za-z0-9]{30,}\b'
    SlackToken = '\bxox(?:b|p|a|r|s)-[A-Za-z0-9-]{20,}\b'
    GoogleApiKey = '\bAIza[0-9A-Za-z_-]{35}\b'
    AzureStorageConnectionString = '(?i)AccountKey\s*=\s*[A-Za-z0-9+/]{40,}={0,2}'
}

$forbiddenTrackedExtensions = @(
    '.bvb',
    '.csv',
    '.key',
    '.p12',
    '.pem',
    '.pfx',
    '.snk',
    '.ybckey'
)

$findings = [System.Collections.Generic.List[string]]::new()
$files = [System.Collections.Generic.List[IO.FileInfo]]::new()
$pendingDirectories = [System.Collections.Generic.Stack[IO.DirectoryInfo]]::new()
$pendingDirectories.Push((Get-Item -LiteralPath $resolvedRoot))

while ($pendingDirectories.Count -gt 0) {
    $directory = $pendingDirectories.Pop()
    foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -File) {
        if (
            $file.FullName -ne $selfPath -and
            $file.Length -le 2MB -and
            $textExtensions -contains $file.Extension.ToLowerInvariant()
        ) {
            $files.Add($file)
        }
    }

    foreach (
        $childDirectory in
        Get-ChildItem -LiteralPath $directory.FullName -Directory
    ) {
        if (
            $excludedDirectoryNames -notcontains $childDirectory.Name -and
            -not ($childDirectory.Attributes -band
                [IO.FileAttributes]::ReparsePoint)
        ) {
            $pendingDirectories.Push($childDirectory)
        }
    }
}

foreach ($file in $files) {
    $content = [IO.File]::ReadAllText($file.FullName)

    foreach ($rule in $rules.GetEnumerator()) {
        if ([Text.RegularExpressions.Regex]::IsMatch($content, $rule.Value)) {
            $relativePath = [IO.Path]::GetRelativePath($resolvedRoot, $file.FullName)
            $findings.Add("$relativePath [$($rule.Key)]")
        }
    }
}

$gitDirectory = Join-Path -Path $resolvedRoot -ChildPath '.git'
if (Test-Path -LiteralPath $gitDirectory) {
    $trackedFiles = @(& git -C $resolvedRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw 'Gitの追跡ファイル一覧を取得できませんでした。'
    }

    foreach ($trackedFile in $trackedFiles) {
        $extension = [IO.Path]::GetExtension($trackedFile).ToLowerInvariant()
        if ($forbiddenTrackedExtensions -contains $extension) {
            $findings.Add("$trackedFile [ForbiddenTrackedFile]")
        }
    }

    if ($IncludeGitHistory) {
        $historicalObjects = @(& git -C $resolvedRoot rev-list --objects --all)
        if ($LASTEXITCODE -ne 0) {
            throw 'Git履歴を取得できませんでした。'
        }

        foreach ($historicalObject in $historicalObjects) {
            $parts = $historicalObject -split ' ', 2
            if ($parts.Count -ne 2) {
                continue
            }

            $objectId = $parts[0]
            $objectPath = $parts[1]
            $extension = [IO.Path]::GetExtension($objectPath).ToLowerInvariant()
            if ($forbiddenTrackedExtensions -contains $extension) {
                $findings.Add("$objectId/$objectPath [ForbiddenHistoricalFile]")
            }

            if ($textExtensions -notcontains $extension) {
                continue
            }

            $objectSize = & git -C $resolvedRoot cat-file -s $objectId
            if ($LASTEXITCODE -ne 0) {
                throw "Gitオブジェクトのサイズを取得できませんでした: $objectId"
            }
            if ([long] $objectSize -gt 2MB) {
                continue
            }

            $historicalContent = (& git -C $resolvedRoot cat-file blob $objectId) -join "`n"
            if ($LASTEXITCODE -ne 0) {
                throw "Gitオブジェクトを読み取れませんでした: $objectId"
            }
            foreach ($rule in $rules.GetEnumerator()) {
                if ([Text.RegularExpressions.Regex]::IsMatch(
                    $historicalContent,
                    $rule.Value
                )) {
                    $findings.Add("$objectId/$objectPath [$($rule.Key):GitHistory]")
                }
            }
        }
    }
}

$containerServicePath = Join-Path -Path $resolvedRoot -ChildPath (
    'src\Ytec.BrowserCapsule.Infrastructure\BackupContainers\BvbBackupContainerService.cs'
)
if (-not (Test-Path -LiteralPath $containerServicePath)) {
    $findings.Add('BvbBackupContainerService.cs [RecoveryKeyGenerationMissing]')
}
else {
    $containerService = [IO.File]::ReadAllText($containerServicePath)
    if ($containerService -notmatch (
        'RandomNumberGenerator\.GetBytes\s*\(\s*RecoveryKeyFileCodec\.KeyLength\s*\)'
    )) {
        $findings.Add(
            'BvbBackupContainerService.cs [RecoveryKeyGenerationMissing]'
        )
    }
}

if ($findings.Count -gt 0) {
    Write-Error ("秘密情報の可能性があるパターンを検出しました。値は表示しません。`n" +
        ($findings -join [Environment]::NewLine))
}

$historyMessage = if ($IncludeGitHistory) {
    '、Git全履歴を確認'
}
else {
    ''
}
Write-Host (
    "秘密情報・鍵スキャン: $($files.Count) ファイル$historyMessage、検出 0 件"
)
