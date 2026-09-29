namespace Ytec.BrowserCapsule.Application.BrowserDiscovery;

/// <summary>
/// 1回のブラウザー検出結果です。
/// </summary>
public sealed record BrowserDiscoverySnapshot(
    IReadOnlyList<BrowserProfileOverview> Profiles,
    IReadOnlyList<BrowserDiscoveryIssue> Issues,
    DateTimeOffset CompletedAtUtc);
