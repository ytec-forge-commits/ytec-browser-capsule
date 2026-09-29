namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

public sealed record BrowserProcessCloseResult(
    int MatchedProcessCount,
    int CloseRequestedCount,
    int ExitedProcessCount,
    int RemainingProcessCount);

/// <summary>
/// 現在のWindowsセッションにある、完全パス一致のブラウザーだけを終了します。
/// </summary>
public interface IBrowserProcessController
{
  Task<BrowserProcessCloseResult> RequestCloseAsync(
      IReadOnlyList<ProcessMatchRule> rules,
      TimeSpan waitTimeout,
      CancellationToken cancellationToken);

  Task<BrowserProcessCloseResult> TerminateRemainingAsync(
      IReadOnlyList<ProcessMatchRule> rules,
      TimeSpan waitTimeout,
      CancellationToken cancellationToken);
}
