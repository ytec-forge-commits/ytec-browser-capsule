namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// 対象ブラウザーが実行中かを読み取り専用で確認します。
/// </summary>
public interface IBrowserProcessInspector
{
  Task<bool> IsRunningAsync(
      IReadOnlyList<ProcessMatchRule> rules,
      CancellationToken cancellationToken);
}
