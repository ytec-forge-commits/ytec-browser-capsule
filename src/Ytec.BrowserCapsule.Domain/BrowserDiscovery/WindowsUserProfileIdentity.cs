namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// ブラウザープロファイルを所有するWindowsユーザーの識別情報です。
/// SIDやフルパスそのものはバックアップへ保存しません。
/// </summary>
public sealed record WindowsUserProfileIdentity(
    string StableId,
    string DisplayName,
    string ProfilePath,
    bool IsCurrentUser);
