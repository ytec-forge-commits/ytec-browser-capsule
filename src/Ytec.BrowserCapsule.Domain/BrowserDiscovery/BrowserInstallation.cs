namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// 読み取り可能なWindowsユーザーで利用できるブラウザー構成を表します。
/// </summary>
public sealed record BrowserInstallation(
    string BrowserId,
    string InstallationId,
    string DisplayName,
    string UserDataRoot,
    string? ExecutablePath,
    BrowserChannel Channel,
    WindowsUserProfileIdentity? WindowsUser = null);
