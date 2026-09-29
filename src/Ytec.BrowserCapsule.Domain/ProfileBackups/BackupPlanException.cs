namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// 重要ファイルを安全に計画へ追加できない場合の例外です。
/// </summary>
public sealed class BackupPlanException : Exception
{
  public BackupPlanException(string message)
      : base(message)
  {
  }

  public BackupPlanException(
      string message,
      Exception innerException)
      : base(message, innerException)
  {
  }
}
