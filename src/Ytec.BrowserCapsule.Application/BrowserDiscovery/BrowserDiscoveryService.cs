using System.Collections.Concurrent;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Application.BrowserDiscovery;

/// <summary>
/// 各ブラウザーの検出、実行中判定、容量見積もりをまとめます。
/// </summary>
public sealed class BrowserDiscoveryService
{
  private const int MaxConcurrentSizeEstimates = 3;

  private readonly IReadOnlyList<IBrowserProfileSource> _sources;
  private readonly IProfileSizeEstimator _sizeEstimator;
  private readonly IBrowserProcessInspector _processInspector;

  public BrowserDiscoveryService(
      IReadOnlyList<IBrowserProfileSource> sources,
      IProfileSizeEstimator sizeEstimator,
      IBrowserProcessInspector processInspector)
  {
    ArgumentNullException.ThrowIfNull(sources);
    ArgumentNullException.ThrowIfNull(sizeEstimator);
    ArgumentNullException.ThrowIfNull(processInspector);

    _sources = sources;
    _sizeEstimator = sizeEstimator;
    _processInspector = processInspector;
  }

  public async Task<BrowserDiscoverySnapshot> DiscoverAsync(
      CancellationToken cancellationToken)
  {
    var profiles = new ConcurrentBag<BrowserProfileOverview>();
    var issues = new ConcurrentBag<BrowserDiscoveryIssue>();
    using var estimateGate = new SemaphoreSlim(MaxConcurrentSizeEstimates);

    var sourceTasks = _sources.Select(source => DiscoverSourceAsync(
        source,
        profiles,
        issues,
        estimateGate,
        cancellationToken));

    await Task.WhenAll(sourceTasks).ConfigureAwait(false);

    var orderedProfiles = profiles
        .OrderBy(item => item.BrowserDisplayName, StringComparer.CurrentCulture)
        .ThenByDescending(item =>
            item.Profile.WindowsUser?.IsCurrentUser is not false)
        .ThenBy(
            item => item.Profile.WindowsUser?.DisplayName,
            StringComparer.CurrentCulture)
        .ThenByDescending(item => item.Profile.IsDefault)
        .ThenBy(item => item.Profile.DisplayName, StringComparer.CurrentCulture)
        .ToArray();

    var orderedIssues = issues
        .OrderBy(issue => issue.BrowserDisplayName, StringComparer.CurrentCulture)
        .ThenBy(issue => issue.Message, StringComparer.CurrentCulture)
        .ToArray();

    return new BrowserDiscoverySnapshot(
        orderedProfiles,
        orderedIssues,
        DateTimeOffset.UtcNow);
  }

  private async Task DiscoverSourceAsync(
      IBrowserProfileSource source,
      ConcurrentBag<BrowserProfileOverview> overviews,
      ConcurrentBag<BrowserDiscoveryIssue> issues,
      SemaphoreSlim estimateGate,
      CancellationToken cancellationToken)
  {
    IReadOnlyList<BrowserInstallation> installations;

    try
    {
      installations = await source
          .DiscoverInstallationsAsync(cancellationToken)
          .ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception)
    {
      issues.Add(new BrowserDiscoveryIssue(
          source.DisplayName,
          "ブラウザー設定の場所を確認できませんでした。"));
      return;
    }

    foreach (var installation in installations)
    {
      cancellationToken.ThrowIfCancellationRequested();
      await DiscoverInstallationAsync(
          source,
          installation,
          overviews,
          issues,
          estimateGate,
          cancellationToken).ConfigureAwait(false);
    }
  }

  private async Task DiscoverInstallationAsync(
      IBrowserProfileSource source,
      BrowserInstallation installation,
      ConcurrentBag<BrowserProfileOverview> overviews,
      ConcurrentBag<BrowserDiscoveryIssue> issues,
      SemaphoreSlim estimateGate,
      CancellationToken cancellationToken)
  {
    IReadOnlyList<BrowserProfile> discoveredProfiles;

    try
    {
      discoveredProfiles = await source
          .DiscoverProfilesAsync(installation, cancellationToken)
          .ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception)
    {
      issues.Add(new BrowserDiscoveryIssue(
          source.DisplayName,
          "プロファイル情報を読み取れませんでした。"));
      return;
    }

    var running = await ReadRunningStateAsync(
        source,
        installation,
        issues,
        cancellationToken).ConfigureAwait(false);

    var estimateTasks = discoveredProfiles.Select(profile => AddOverviewAsync(
        source.DisplayName,
        profile,
        running,
        overviews,
        issues,
        estimateGate,
        cancellationToken));

    await Task.WhenAll(estimateTasks).ConfigureAwait(false);
  }

  private async Task<bool> ReadRunningStateAsync(
      IBrowserProfileSource source,
      BrowserInstallation installation,
      ConcurrentBag<BrowserDiscoveryIssue> issues,
      CancellationToken cancellationToken)
  {
    try
    {
      return await _processInspector
          .IsRunningAsync(
              source.GetProcessMatchRules(installation),
              cancellationToken)
          .ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception)
    {
      issues.Add(new BrowserDiscoveryIssue(
          source.DisplayName,
          "ブラウザーの実行状態を確認できませんでした。"));
      return false;
    }
  }

  private async Task AddOverviewAsync(
      string browserDisplayName,
      BrowserProfile profile,
      bool isBrowserRunning,
      ConcurrentBag<BrowserProfileOverview> overviews,
      ConcurrentBag<BrowserDiscoveryIssue> issues,
      SemaphoreSlim estimateGate,
      CancellationToken cancellationToken)
  {
    await estimateGate.WaitAsync(cancellationToken).ConfigureAwait(false);

    try
    {
      var estimate = await _sizeEstimator
          .EstimateAsync(profile.AbsoluteProfilePath, cancellationToken)
          .ConfigureAwait(false);

      overviews.Add(new BrowserProfileOverview(
          browserDisplayName,
          profile,
          estimate,
          isBrowserRunning));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception)
    {
      issues.Add(new BrowserDiscoveryIssue(
          browserDisplayName,
          $"「{profile.DisplayName}」の容量を見積もれませんでした。"));

      overviews.Add(new BrowserProfileOverview(
          browserDisplayName,
          profile,
          new ProfileSizeEstimate(0, 0, 1, false),
          isBrowserRunning));
    }
    finally
    {
      estimateGate.Release();
    }
  }
}
