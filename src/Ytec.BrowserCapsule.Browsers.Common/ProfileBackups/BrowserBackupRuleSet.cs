using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;

/// <summary>
/// 1コンポーネントのファイルまたはディレクトリー起点です。
/// </summary>
public sealed record BackupComponentPathRule(
    BackupComponent Component,
    string RelativePath,
    bool IsRequired);

/// <summary>
/// ブラウザー単位でテスト可能なバックアップ許可・除外設定です。
/// </summary>
public sealed record BrowserBackupRuleSet(
    IReadOnlyList<BackupComponentPathRule> ComponentPaths,
    IReadOnlySet<string> ExcludedDirectoryNames,
    IReadOnlySet<string> ExcludedFileNames,
    IReadOnlySet<string> ExcludedFileSuffixes);
