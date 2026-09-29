using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;

namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// UIから渡す1プロファイルの選択です。
/// </summary>
public sealed record ProfileBackupSelection(
    BrowserProfile Profile,
    BackupComponent Components);

/// <summary>
/// 公式エクスポート後、平文を削除して一時暗号化したCSVです。
/// </summary>
public sealed record ProfilePasswordCsvSelection(
    string SourceProfileId,
    IProtectedPasswordCsv ProtectedCsv);

/// <summary>
/// 複数プロファイルのバックアップ作成要求です。
/// </summary>
public sealed record ProfileBackupRequest(
    string DestinationPath,
    IReadOnlyList<ProfileBackupSelection> Profiles,
    string AppVersion,
    IReadOnlyList<ProfilePasswordCsvSelection>? PasswordCsvFiles = null);

/// <summary>
/// バックアップ進捗の段階です。
/// </summary>
public enum ProfileBackupStage
{
  Planning = 0,
  Writing = 1,
  Verifying = 2,
  Completed = 3,
}

/// <summary>
/// UIへ通知するバックアップ進捗です。
/// </summary>
public sealed record ProfileBackupProgress(
    ProfileBackupStage Stage,
    int CompletedFiles,
    int TotalFiles,
    long ProcessedBytes,
    long TotalBytes,
    string? BrowserId,
    string? ProfileDisplayName);

/// <summary>
/// 警告を含むバックアップ完了結果です。
/// </summary>
public sealed record ProfileBackupResult(
    BackupContainerWriteResult Container,
    IReadOnlyList<BackupPlanWarning> Warnings);

/// <summary>
/// 対象ブラウザーが動作中のため安全に開始できないことを表します。
/// </summary>
public sealed class BrowserMustBeClosedException : Exception
{
  public BrowserMustBeClosedException(string browserDisplayName)
      : base($"{browserDisplayName}を終了してから、もう一度お試しください。")
  {
    BrowserDisplayName = browserDisplayName;
  }

  public string BrowserDisplayName { get; }
}
