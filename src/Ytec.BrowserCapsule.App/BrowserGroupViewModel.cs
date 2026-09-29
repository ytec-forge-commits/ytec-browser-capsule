using Ytec.BrowserCapsule.App.Localization;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// 画面上でブラウザーごとにプロファイルをまとめます。
/// </summary>
public sealed class BrowserGroupViewModel
{
  public BrowserGroupViewModel(
      string browserName,
      string accentColor,
      IReadOnlyList<ProfileItemViewModel> profiles)
  {
    BrowserName = browserName;
    AccentColor = accentColor;
    Profiles = profiles;
  }

  public string BrowserName { get; }

  public string AccentColor { get; }

  public IReadOnlyList<ProfileItemViewModel> Profiles { get; }

  public string ProfileCountText => UiText.F("{0}件", Profiles.Count);
}
