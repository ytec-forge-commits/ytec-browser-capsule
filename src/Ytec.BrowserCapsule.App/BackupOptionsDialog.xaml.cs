using System.Windows;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// バックアップ項目と復元キーの保管責任を確認します。
/// </summary>
public partial class BackupOptionsDialog : Window
{
  public BackupOptionsDialog()
  {
    InitializeComponent();
    UpdateCreateButton();
    UiText.Apply(this);
  }

  public BackupComponent SelectedComponents { get; private set; }

  private void CreateButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var selectedComponents = ReadSelectedComponents();
    if (selectedComponents is BackupComponent.None)
    {
      ValidationText.Text =
          "保存する内容を1つ以上選んでください。";
      return;
    }

    SelectedComponents = selectedComponents;
    DialogResult = true;
  }

  private void CancelButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DialogResult = false;
  }

  private void FullProfileCheckBox_Changed(
      object sender,
      RoutedEventArgs e)
  {
    var useFullProfile = FullProfileCheckBox.IsChecked is true;
    SettingsCheckBox.IsEnabled = !useFullProfile;
    BookmarksCheckBox.IsEnabled = !useFullProfile;
    HistoryCheckBox.IsEnabled = !useFullProfile;
    ExtensionsCheckBox.IsEnabled = !useFullProfile;
    SiteDataCheckBox.IsEnabled = !useFullProfile;
    SessionsCheckBox.IsEnabled = !useFullProfile;
    UpdateCreateButton();
  }

  private void AcknowledgementCheckBox_Changed(
      object sender,
      RoutedEventArgs e)
  {
    UpdateCreateButton();
  }

  private BackupComponent ReadSelectedComponents()
  {
    if (FullProfileCheckBox.IsChecked is true)
    {
      return BackupComponent.FullProfile
          | (PasswordCsvCheckBox.IsChecked is true
              ? BackupComponent.PasswordCsv
              : BackupComponent.None);
    }

    var result = BackupComponent.None;
    result |= SettingsCheckBox.IsChecked is true
        ? BackupComponent.Settings
        : BackupComponent.None;
    result |= BookmarksCheckBox.IsChecked is true
        ? BackupComponent.Bookmarks
        : BackupComponent.None;
    result |= HistoryCheckBox.IsChecked is true
        ? BackupComponent.History
        : BackupComponent.None;
    result |= ExtensionsCheckBox.IsChecked is true
        ? BackupComponent.Extensions
        : BackupComponent.None;
    result |= SiteDataCheckBox.IsChecked is true
        ? BackupComponent.CookiesAndSiteData
        : BackupComponent.None;
    result |= SessionsCheckBox.IsChecked is true
        ? BackupComponent.Sessions
        : BackupComponent.None;
    result |= PasswordCsvCheckBox.IsChecked is true
        ? BackupComponent.PasswordCsv
        : BackupComponent.None;
    return result;
  }

  private void UpdateCreateButton()
  {
    if (!IsInitialized)
    {
      return;
    }

    CreateButton.IsEnabled =
        AcknowledgementCheckBox.IsChecked is true;
  }
}
