namespace Ytec.BrowserCapsule.Domain.BackupContainers;

/// <summary>
/// 暗号化バックアップを安全に処理できない場合の基底例外です。
/// </summary>
public class BackupContainerException : Exception
{
  public BackupContainerException(string message)
      : base(message)
  {
  }

  public BackupContainerException(
      string message,
      Exception innerException)
      : base(message, innerException)
  {
  }
}

/// <summary>
/// 誤資格情報、改ざん、切断を区別せず拒否する例外です。
/// </summary>
public sealed class BackupContainerIntegrityException
    : BackupContainerException
{
  public BackupContainerIntegrityException()
      : base("バックアップの復元キー、パスフレーズ、または完全性を確認できません。")
  {
  }

  public BackupContainerIntegrityException(Exception innerException)
      : base(
          "バックアップの復元キー、パスフレーズ、または完全性を確認できません。",
          innerException)
  {
  }
}

/// <summary>
/// 安全でないコンテナ内相対パスを拒否する例外です。
/// </summary>
public sealed class InvalidBackupEntryPathException
    : BackupContainerException
{
  public InvalidBackupEntryPathException(string message)
      : base(message)
  {
  }
}
