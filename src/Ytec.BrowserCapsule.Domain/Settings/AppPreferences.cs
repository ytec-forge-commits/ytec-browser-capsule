namespace Ytec.BrowserCapsule.Domain.Settings;

/// <summary>
/// 端末内だけに保存する、秘密を含まない利用者設定です。
/// </summary>
public sealed record AppPreferences(
    string? DefaultBackupDirectory,
    AppLanguage Language = AppLanguage.SystemDefault)
{
  /// <summary>
  /// 初回起動時の既定値です。
  /// </summary>
  public static AppPreferences Default { get; } =
      new(
          DefaultBackupDirectory: null,
          Language: AppLanguage.SystemDefault);
}

/// <summary>
/// アプリの表示言語です。
/// </summary>
public enum AppLanguage
{
  /// <summary>
  /// Windowsの表示言語に合わせます。
  /// </summary>
  SystemDefault = 0,

  /// <summary>
  /// 日本語で表示します。
  /// </summary>
  Japanese = 1,

  /// <summary>
  /// 英語で表示します。
  /// </summary>
  English = 2,
}

/// <summary>
/// 利用者設定をローカルへ読み書きします。
/// </summary>
public interface IAppPreferencesStore
{
  /// <summary>
  /// 保存済み設定を読み取ります。
  /// </summary>
  Task<AppPreferences> LoadAsync(CancellationToken cancellationToken);

  /// <summary>
  /// 設定を原子的に保存します。
  /// </summary>
  Task SaveAsync(
      AppPreferences preferences,
      CancellationToken cancellationToken);
}
