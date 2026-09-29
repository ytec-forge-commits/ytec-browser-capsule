namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// プロファイル容量の読み取り専用見積もり結果です。
/// </summary>
public sealed record ProfileSizeEstimate(
    long Bytes,
    long FileCount,
    int SkippedEntryCount,
    bool IsComplete);
