namespace Ytec.BrowserCapsule.Domain.BackupContainers;

public enum BackupContainerCredentialKind
{
  LegacyPassphrase = 1,
  RecoveryKeyFile = 2,
}

/// <summary>
/// 暗号化バックアップを開くための資格情報です。
/// 値やファイル内容を文字列化・ログ出力しないでください。
/// </summary>
public sealed class BackupContainerCredential
{
  private BackupContainerCredential(
      BackupContainerCredentialKind kind,
      ReadOnlyMemory<char> legacyPassphrase,
      string? recoveryKeyFilePath)
  {
    Kind = kind;
    LegacyPassphrase = legacyPassphrase;
    RecoveryKeyFilePath = recoveryKeyFilePath;
  }

  public BackupContainerCredentialKind Kind { get; }

  public ReadOnlyMemory<char> LegacyPassphrase { get; }

  public string? RecoveryKeyFilePath { get; }

  public static BackupContainerCredential FromLegacyPassphrase(
      ReadOnlyMemory<char> passphrase)
  {
    if (passphrase.IsEmpty)
    {
      throw new ArgumentException(
          "パスフレーズを入力してください。",
          nameof(passphrase));
    }

    return new BackupContainerCredential(
        BackupContainerCredentialKind.LegacyPassphrase,
        passphrase,
        recoveryKeyFilePath: null);
  }

  public static BackupContainerCredential FromRecoveryKeyFile(
      string recoveryKeyFilePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(recoveryKeyFilePath);
    return new BackupContainerCredential(
        BackupContainerCredentialKind.RecoveryKeyFile,
        ReadOnlyMemory<char>.Empty,
        Path.GetFullPath(recoveryKeyFilePath));
  }

  public override string ToString()
  {
    return Kind.ToString();
  }
}
