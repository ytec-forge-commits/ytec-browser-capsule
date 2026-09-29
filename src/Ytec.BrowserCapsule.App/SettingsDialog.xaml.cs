using System.IO;
using System.Windows;
using Microsoft.Win32;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Domain.Settings;
using Ytec.BrowserCapsule.Infrastructure.Settings;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// 秘密を含まないローカル利用者設定を編集します。
/// </summary>
public partial class SettingsDialog : Window
{
  public SettingsDialog(AppPreferences preferences)
  {
    ArgumentNullException.ThrowIfNull(preferences);
    InitializeComponent();
    DefaultBackupDirectoryTextBox.Text =
        preferences.DefaultBackupDirectory ?? string.Empty;
    LanguageComboBox.SelectedValue = preferences.Language.ToString();
    UiText.Apply(this);
  }

  public AppPreferences? SelectedPreferences { get; private set; }

  private void ChooseDirectoryButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var dialog = new OpenFolderDialog
    {
      Multiselect = false,
      Title = UiText.T("既定のバックアップ先を選択"),
    };
    if (Directory.Exists(DefaultBackupDirectoryTextBox.Text))
    {
      dialog.InitialDirectory = DefaultBackupDirectoryTextBox.Text;
    }

    if (dialog.ShowDialog(this) is true)
    {
      DefaultBackupDirectoryTextBox.Text = dialog.FolderName;
      ValidationText.Text = string.Empty;
    }
  }

  private void ResetDirectoryButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DefaultBackupDirectoryTextBox.Clear();
    ValidationText.Text = string.Empty;
  }

  private void SaveButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    try
    {
      var normalized =
          JsonAppPreferencesStore.NormalizeDefaultBackupDirectory(
              DefaultBackupDirectoryTextBox.Text);
      if (normalized is not null && !Directory.Exists(normalized))
      {
        ValidationText.Text =
            "存在するローカルフォルダーを選んでください。";
        return;
      }

      var language = Enum.TryParse<AppLanguage>(
          LanguageComboBox.SelectedValue as string,
          ignoreCase: false,
          out var selectedLanguage)
          ? selectedLanguage
          : AppLanguage.SystemDefault;
      SelectedPreferences = new AppPreferences(normalized, language);
      DialogResult = true;
    }
    catch (ArgumentException exception)
    {
      ValidationText.Text = exception.Message;
    }
  }

  private void CancelButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DialogResult = false;
  }
}
