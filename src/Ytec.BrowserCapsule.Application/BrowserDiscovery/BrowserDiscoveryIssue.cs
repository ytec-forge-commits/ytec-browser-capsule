namespace Ytec.BrowserCapsule.Application.BrowserDiscovery;

/// <summary>
/// 検出を継続できる非致命的な問題を表します。
/// </summary>
public sealed record BrowserDiscoveryIssue(
    string BrowserDisplayName,
    string Message);
