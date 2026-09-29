using System.Windows;
using Ytec.BrowserCapsule.App.Localization;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// 製品情報、セキュリティ境界、ライセンス表示を案内します。
/// </summary>
public partial class AboutDialog : Window
{
  public AboutDialog()
  {
    InitializeComponent();
    var version = typeof(AboutDialog).Assembly
        .GetName()
        .Version?.ToString(3)
        ?? "1.2.1";
    VersionText.Text = UiText.F("バージョン {0}", version);
    UiText.Apply(this);
  }

  private void CloseButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    Close();
  }
}
