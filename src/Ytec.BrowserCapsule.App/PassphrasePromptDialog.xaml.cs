using System.Windows;
using Ytec.BrowserCapsule.App.Localization;

namespace Ytec.BrowserCapsule.App;

public partial class PassphrasePromptDialog : Window
{
  public PassphrasePromptDialog()
  {
    InitializeComponent();
    UiText.Apply(this);
  }

  public char[]? PassphraseCharacters { get; private set; }

  private void PassphraseBox_Changed(
      object sender,
      RoutedEventArgs e)
  {
    OpenButton.IsEnabled = PassphraseBox.Password.Length > 0;
    ValidationText.Text = string.Empty;
  }

  private void OpenButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    if (PassphraseBox.Password.Length == 0)
    {
      ValidationText.Text = "パスフレーズを入力してください。";
      return;
    }

    PassphraseCharacters = PassphraseBox.Password.ToCharArray();
    PassphraseBox.Clear();
    DialogResult = true;
  }

  private void CancelButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DialogResult = false;
  }
}
