namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// ブラウザーの1プロファイルを表します。
/// </summary>
public sealed record BrowserProfile(
    string BrowserId,
    string InstallationId,
    string ProfileId,
    string DisplayName,
    string ProfileDirectoryName,
    string AbsoluteProfilePath,
    string UserDataRoot,
    bool IsDefault,
    DateTimeOffset? LastUsedUtc,
    BrowserChannel Channel,
    ProfileDiscoveryConfidence Confidence,
    WindowsUserProfileIdentity? WindowsUser = null);
