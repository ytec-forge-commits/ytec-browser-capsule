using System.Windows;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// メッセージ本文と見出しを表示言語へ変換してから表示します。
/// </summary>
internal static class MessageBox
{
  public static MessageBoxResult Show(
      string messageBoxText,
      string caption,
      MessageBoxButton button,
      MessageBoxImage icon)
  {
    return System.Windows.MessageBox.Show(
        Localization.UiText.T(messageBoxText),
        Localization.UiText.T(caption),
        button,
        icon);
  }

  public static MessageBoxResult Show(
      Window owner,
      string messageBoxText,
      string caption,
      MessageBoxButton button,
      MessageBoxImage icon)
  {
    return System.Windows.MessageBox.Show(
        owner,
        Localization.UiText.T(messageBoxText),
        Localization.UiText.T(caption),
        button,
        icon);
  }

  public static MessageBoxResult Show(
      Window owner,
      string messageBoxText,
      string caption,
      MessageBoxButton button,
      MessageBoxImage icon,
      MessageBoxResult defaultResult)
  {
    return System.Windows.MessageBox.Show(
        owner,
        Localization.UiText.T(messageBoxText),
        Localization.UiText.T(caption),
        button,
        icon,
        defaultResult);
  }
}
