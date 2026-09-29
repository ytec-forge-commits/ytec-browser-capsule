namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// ブラウザー固有のプロファイル検出処理を抽象化します。
/// </summary>
public interface IBrowserProfileSource
{
  string BrowserId { get; }

  string DisplayName { get; }

  Task<IReadOnlyList<BrowserInstallation>> DiscoverInstallationsAsync(
      CancellationToken cancellationToken);

  Task<IReadOnlyList<BrowserProfile>> DiscoverProfilesAsync(
      BrowserInstallation installation,
      CancellationToken cancellationToken);

  IReadOnlyList<ProcessMatchRule> GetProcessMatchRules(
      BrowserInstallation installation);
}
