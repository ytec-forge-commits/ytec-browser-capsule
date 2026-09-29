using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Application.BrowserDiscovery;

/// <summary>
/// UIへ渡すプロファイル検出結果です。
/// </summary>
public sealed record BrowserProfileOverview(
    string BrowserDisplayName,
    BrowserProfile Profile,
    ProfileSizeEstimate Size,
    bool IsBrowserRunning);
