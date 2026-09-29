namespace Ytec.BrowserCapsule.Infrastructure.ApplicationLifecycle;

/// <summary>
/// 同じWindowsセッションでアプリが重複起動しないようにします。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
  private readonly string _mutexName;
  private Mutex? _ownedMutex;

  /// <summary>
  /// 指定した名前の単一起動ガードを作成します。
  /// </summary>
  /// <param name="mutexName">
  /// アプリ固有の名前付きMutex名。
  /// </param>
  public SingleInstanceGuard(string mutexName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
    _mutexName = mutexName;
  }

  /// <summary>
  /// このプロセスが単一起動権を取得できた場合に<c>true</c>を返します。
  /// </summary>
  public bool TryAcquire()
  {
    if (_ownedMutex is not null)
    {
      return true;
    }

    var candidate = new Mutex(
        initiallyOwned: true,
        _mutexName,
        out var createdNew);
    if (!createdNew)
    {
      candidate.Dispose();
      return false;
    }

    _ownedMutex = candidate;
    return true;
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_ownedMutex is null)
    {
      return;
    }

    _ownedMutex.ReleaseMutex();
    _ownedMutex.Dispose();
    _ownedMutex = null;
  }
}
