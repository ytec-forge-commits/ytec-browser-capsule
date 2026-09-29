using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;

/// <summary>
/// Chromium系ブラウザーごとの差分設定です。
/// </summary>
public sealed record ChromiumBrowserConfiguration(
    string BrowserId,
    string DisplayName,
    string PolicySubKey,
    string StandardUserDataRoot,
    IReadOnlyList<string> ExecutableCandidates,
    string ProcessName,
    BrowserChannel Channel);
