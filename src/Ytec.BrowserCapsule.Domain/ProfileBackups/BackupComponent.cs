namespace Ytec.BrowserCapsule.Domain.ProfileBackups;

/// <summary>
/// ユーザーが選択できるバックアップ単位です。
/// </summary>
[Flags]
public enum BackupComponent
{
  None = 0,
  Settings = 1 << 0,
  Bookmarks = 1 << 1,
  History = 1 << 2,
  Extensions = 1 << 3,
  CookiesAndSiteData = 1 << 4,
  Sessions = 1 << 5,
  PasswordCsv = 1 << 6,
  FullProfile = 1 << 7,
}
