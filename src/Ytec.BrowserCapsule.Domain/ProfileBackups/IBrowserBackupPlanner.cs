using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// ブラウザー固有の許可・除外規則からバックアップ計画を作成します。
/// </summary>
public interface IBrowserBackupPlanner
{
  string BrowserId { get; }

  Task<BrowserBackupPlan> BuildBackupPlanAsync(
      BrowserProfile profile,
      string backupProfileId,
      BackupComponent selection,
      CancellationToken cancellationToken);
}
