using System.Text.Json;
using Ytec.BrowserCapsule.Domain.Settings;

namespace Ytec.BrowserCapsule.Infrastructure.Settings;

/// <summary>
/// 利用者設定をLocalAppData配下のJSONへ原子的に保存します。
/// </summary>
public sealed class JsonAppPreferencesStore : IAppPreferencesStore
{
  private const int CurrentSchemaVersion = 2;
  private readonly string _settingsPath;

  /// <summary>
  /// 現在ユーザーの標準設定パスを使用します。
  /// </summary>
  public JsonAppPreferencesStore()
      : this(Path.Combine(
          Environment.GetFolderPath(
              Environment.SpecialFolder.LocalApplicationData),
          "Y-TEC",
          "BrowserCapsule"))
  {
  }

  /// <summary>
  /// 指定した設定ルートを使用します。
  /// </summary>
  public JsonAppPreferencesStore(string settingsRoot)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(settingsRoot);
    _settingsPath = Path.Combine(
        Path.GetFullPath(settingsRoot),
        "settings.json");
  }

  /// <inheritdoc />
  public async Task<AppPreferences> LoadAsync(
      CancellationToken cancellationToken)
  {
    if (!File.Exists(_settingsPath))
    {
      return AppPreferences.Default;
    }

    await using var stream = new FileStream(
        _settingsPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(
        stream,
        cancellationToken: cancellationToken);
    if (document is null
        || document.SchemaVersion is < 1 or > CurrentSchemaVersion)
    {
      return AppPreferences.Default;
    }

    try
    {
      return new AppPreferences(
          NormalizeDefaultBackupDirectory(
              document.DefaultBackupDirectory),
          ParseLanguage(document.Language));
    }
    catch (ArgumentException)
    {
      return AppPreferences.Default;
    }
  }

  /// <inheritdoc />
  public async Task SaveAsync(
      AppPreferences preferences,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(preferences);
    var normalized = NormalizeDefaultBackupDirectory(
        preferences.DefaultBackupDirectory);
    var directory = Path.GetDirectoryName(_settingsPath)
        ?? throw new InvalidOperationException(
            "設定フォルダーを決定できません。");
    Directory.CreateDirectory(directory);

    var partialPath = _settingsPath + ".partial";
    try
    {
      await using (var stream = new FileStream(
          partialPath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 4096,
          FileOptions.Asynchronous | FileOptions.WriteThrough))
      {
        await JsonSerializer.SerializeAsync(
            stream,
            new SettingsDocument(
                CurrentSchemaVersion,
                normalized,
                FormatLanguage(preferences.Language)),
            cancellationToken: cancellationToken);
        await stream.FlushAsync(cancellationToken);
      }

      File.Move(
          partialPath,
          _settingsPath,
          overwrite: true);
    }
    finally
    {
      if (File.Exists(partialPath))
      {
        File.Delete(partialPath);
      }
    }
  }

  /// <summary>
  /// 既定保存先を、ローカルの完全修飾パスへ正規化します。
  /// </summary>
  public static string? NormalizeDefaultBackupDirectory(
      string? directory)
  {
    if (string.IsNullOrWhiteSpace(directory))
    {
      return null;
    }

    var trimmed = directory.Trim();
    if (!Path.IsPathFullyQualified(trimmed)
        || IsNetworkOrDevicePath(trimmed))
    {
      throw new ArgumentException(
          "既定保存先にはローカルの完全修飾パスを指定してください。",
          nameof(directory));
    }

    var fullPath = Path.GetFullPath(trimmed);
    if (IsNetworkOrDevicePath(fullPath))
    {
      throw new ArgumentException(
          "ネットワークパスとデバイスパスは既定保存先にできません。",
          nameof(directory));
    }

    return fullPath;
  }

  private static bool IsNetworkOrDevicePath(string path)
  {
    return path.StartsWith(@"\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);
  }

  private static AppLanguage ParseLanguage(string? value)
  {
    return value?.Trim().ToLowerInvariant() switch
    {
      "ja" => AppLanguage.Japanese,
      "en" => AppLanguage.English,
      _ => AppLanguage.SystemDefault,
    };
  }

  private static string FormatLanguage(AppLanguage language)
  {
    return language switch
    {
      AppLanguage.Japanese => "ja",
      AppLanguage.English => "en",
      _ => "system",
    };
  }

  private sealed record SettingsDocument(
      int SchemaVersion,
      string? DefaultBackupDirectory,
      string? Language = null);
}
