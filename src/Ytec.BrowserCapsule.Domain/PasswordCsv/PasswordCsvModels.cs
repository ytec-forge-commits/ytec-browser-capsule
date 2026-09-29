using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Domain.PasswordCsv;

/// <summary>
/// CSVの値を保持せずに返す構造検証結果です。
/// </summary>
public sealed record PasswordCsvValidationResult(
    IReadOnlyList<string> HeaderNames,
    long RecordCount,
    long FileBytes);

/// <summary>
/// 平文CSVを即時暗号化した、一時的な読み取り元です。
/// </summary>
public interface IProtectedPasswordCsv : IAsyncDisposable
{
  PasswordCsvValidationResult Validation { get; }

  long PlaintextLength { get; }

  string? ResidualPlaintextPath { get; }

  ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 現在のWindowsユーザーだけが利用できる一時作業領域です。
/// </summary>
public interface IPasswordCsvStagingSession : IAsyncDisposable
{
  string StagingDirectory { get; }

  Task<string?> WaitForStableCsvAsync(
      TimeSpan timeout,
      CancellationToken cancellationToken);

  Task<IProtectedPasswordCsv> ProtectAsync(
      string csvPath,
      bool allowEmpty,
      CancellationToken cancellationToken);
}

/// <summary>
/// CSV用の制限ACL一時領域を作成し、前回クラッシュ残骸を清掃します。
/// </summary>
public interface IPasswordCsvStagingFactory
{
  Task CleanupStaleSessionsAsync(CancellationToken cancellationToken);

  Task<IPasswordCsvStagingSession> CreateAsync(
      CancellationToken cancellationToken);
}

/// <summary>
/// ブラウザー標準のパスワード管理画面だけを開きます。
/// </summary>
public interface IBrowserPasswordPageLauncher
{
  Task OpenExportPageAsync(
      BrowserProfile profile,
      CancellationToken cancellationToken);

  Task OpenImportPageAsync(
      BrowserProfile profile,
      CancellationToken cancellationToken);
}

/// <summary>
/// CSV形式が安全に扱えないことを示します。
/// </summary>
public sealed class PasswordCsvValidationException : Exception
{
  public PasswordCsvValidationException(string message)
      : base(message)
  {
  }

  public PasswordCsvValidationException(
      string message,
      Exception innerException)
      : base(message, innerException)
  {
  }
}
