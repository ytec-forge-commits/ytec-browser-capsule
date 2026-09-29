namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// プロファイル検出結果の確からしさを表します。
/// </summary>
public enum ProfileDiscoveryConfidence
{
  Metadata = 0,
  DirectoryFallback = 1,
}
