namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// プロファイルの容量を、リンク先を追跡せず見積もります。
/// </summary>
public interface IProfileSizeEstimator
{
  Task<ProfileSizeEstimate> EstimateAsync(
      string profilePath,
      CancellationToken cancellationToken);
}
