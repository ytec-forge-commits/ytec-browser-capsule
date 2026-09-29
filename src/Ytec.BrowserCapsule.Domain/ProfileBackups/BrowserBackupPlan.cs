using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// 計画時点で固定した1ファイルです。
/// </summary>
public sealed record BackupPlanFile(
    string SourcePath,
    string EntryPath,
    long Length,
    DateTimeOffset LastWriteUtc,
    BackupComponent Component,
    bool IsRequired);

/// <summary>
/// スキップした任意ファイルなどの非致命的な注意事項です。
/// </summary>
public sealed record BackupPlanWarning(
    string Code,
    string Message);

/// <summary>
/// 1プロファイルの固定済みバックアップ計画です。
/// </summary>
public sealed record BrowserBackupPlan(
    BrowserProfile Profile,
    string BackupProfileId,
    BackupComponent SelectedComponents,
    IReadOnlyList<BackupPlanFile> Files,
    IReadOnlyList<BackupPlanWarning> Warnings,
    long TotalBytes);
