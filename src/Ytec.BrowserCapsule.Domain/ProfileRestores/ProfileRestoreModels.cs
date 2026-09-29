using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Domain.ProfileRestores;

public enum ProfileRestoreMode
{
  Migration = 0,
  SameEnvironment = 1,
}

/// <summary>
/// 暗号化バックアップ元と、現在のブラウザープロファイルの明示対応です。
/// </summary>
public sealed record ProfileRestoreMapping(
    string BackupProfileId,
    BrowserProfile TargetProfile,
    BackupComponent Components);

/// <summary>
/// 検証、ロールバック、ステージング、適用を行う復元要求です。
/// </summary>
public sealed record ProfileRestoreRequest(
    string ContainerPath,
    IReadOnlyList<ProfileRestoreMapping> Mappings,
    ProfileRestoreMode RequestedMode,
    bool AllowFullRestoreAcrossEnvironment,
    string RollbackDirectory,
    string AppVersion);

public enum ProfileRestoreStage
{
  Inspecting = 0,
  CreatingRollback = 1,
  Extracting = 2,
  Applying = 3,
  Verifying = 4,
  RollingBack = 5,
  Completed = 6,
}

public sealed record ProfileRestoreProgress(
    ProfileRestoreStage Stage,
    int CompletedFiles,
    int TotalFiles,
    string? ProfileDisplayName);

public sealed record ProfileRestoreWarning(
    string Code,
    string Message);

public sealed record ProfileRestoreResult(
    ProfileRestoreMode EffectiveMode,
    bool EnvironmentMatched,
    string? RollbackPath,
    int RestoredFileCount,
    IReadOnlyList<ProfileRestoreWarning> Warnings,
    BackupContainerVerificationResult SourceVerification)
{
  public string? RollbackRecoveryKeyPath { get; init; }
}

public sealed record PasswordCsvImportFile(
    string BackupProfileId,
    string BrowserId,
    string AbsolutePath,
    long RecordCount);

/// <summary>
/// 一時展開済みファイルをプロファイルへ安全に置換する1操作です。
/// </summary>
public sealed record RestoreFileOperation(
    string SourcePath,
    string TargetRoot,
    string RelativeTargetPath,
    long Length,
    string Sha256,
    string? Component,
    string TargetProfileDisplayName)
{
  public string? BrowserId { get; init; }
}

public interface IRestoreFileApplier
{
  Task ApplyAsync(
      IReadOnlyList<RestoreFileOperation> operations,
      Func<CancellationToken, Task> ensureBrowserClosedAsync,
      IProgress<ProfileRestoreProgress>? progress,
      CancellationToken cancellationToken);
}

public sealed class RestorePlanException : Exception
{
  public RestorePlanException(string message)
      : base(message)
  {
  }

  public RestorePlanException(
      string message,
      Exception innerException)
      : base(message, innerException)
  {
  }
}

public sealed class RestoreFailedRolledBackException : Exception
{
  public RestoreFailedRolledBackException(Exception restoreFailure)
      : base(
          "復元に失敗しましたが、復元前の状態へ戻しました。",
          restoreFailure)
  {
  }
}

public sealed class RestoreFailedRollbackFailedException : Exception
{
  public RestoreFailedRollbackFailedException(
      Exception restoreFailure,
      Exception rollbackFailure)
      : base(
          "復元と自動ロールバックの両方に失敗しました。ロールバックファイルを保持してサポートへ連絡してください。",
          new AggregateException(restoreFailure, rollbackFailure))
  {
  }
}
