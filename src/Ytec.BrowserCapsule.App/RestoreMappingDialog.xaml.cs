using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Application.ProfileRestores;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.App;

public partial class RestoreMappingDialog : Window
{
  private readonly bool _environmentMatched;
  private readonly List<RestoreMappingRow> _rows;

  public RestoreMappingDialog(
      BackupContainerVerificationResult verification,
      IReadOnlyList<BrowserProfile> currentProfiles,
      bool environmentMatched)
  {
    ArgumentNullException.ThrowIfNull(verification);
    ArgumentNullException.ThrowIfNull(currentProfiles);
    _environmentMatched = environmentMatched;
    _rows = verification.Manifest.Profiles.Select(source =>
    {
      var targets = currentProfiles
          .Where(profile => string.Equals(
              profile.BrowserId,
              source.BrowserId,
              StringComparison.Ordinal))
          .Select(profile => new RestoreTargetOption(
              profile,
              BuildTargetDisplayLabel(profile)))
          .ToArray();
      var sourceWindowsUser = string.IsNullOrWhiteSpace(
          source.SourceWindowsUserDisplayName)
          ? string.Empty
          : $" / Windows: {source.SourceWindowsUserDisplayName}";
      return new RestoreMappingRow(
          source.BackupProfileId,
          $"{GetBrowserDisplayName(source.BrowserId)} / "
          + $"{source.DisplayName}{sourceWindowsUser}",
          targets);
    }).ToList();

    InitializeComponent();
    MappingItemsControl.ItemsSource = _rows;
    ConfigureAvailableComponents(
        RestoreComponentSelectionPolicy.GetAvailableComponents(
            verification.Manifest));
    if (environmentMatched)
    {
      SameEnvironmentRadioButton.IsChecked = true;
    }
    else
    {
      MigrationRadioButton.IsChecked = true;
    }

    UpdateEnvironmentWarning();
    UiText.Apply(this);
  }

  public IReadOnlyList<ProfileRestoreMapping> Mappings { get; private set; }
      = [];

  public ProfileRestoreMode SelectedMode { get; private set; }

  public bool AllowFullRestoreAcrossEnvironment { get; private set; }

  public bool ImportPasswordCsv =>
      PasswordCsvCheckBox.IsChecked is true;

  private void ConfigureAvailableComponents(
      BackupComponent available)
  {
    ConfigureComponent(
        SettingsCheckBox,
        available,
        BackupComponent.Settings);
    ConfigureComponent(
        BookmarksCheckBox,
        available,
        BackupComponent.Bookmarks);
    ConfigureComponent(
        HistoryCheckBox,
        available,
        BackupComponent.History);
    ConfigureComponent(
        ExtensionsCheckBox,
        available,
        BackupComponent.Extensions);
    ConfigureComponent(
        SiteDataCheckBox,
        available,
        BackupComponent.CookiesAndSiteData);
    ConfigureComponent(
        SessionsCheckBox,
        available,
        BackupComponent.Sessions);
    ConfigureComponent(
        FullProfileCheckBox,
        available,
        BackupComponent.FullProfile);
    ConfigureComponent(
        PasswordCsvCheckBox,
        available,
        BackupComponent.PasswordCsv);
    PasswordCsvCheckBox.IsChecked =
        PasswordCsvCheckBox.IsEnabled;
  }

  private static void ConfigureComponent(
      System.Windows.Controls.CheckBox checkBox,
      BackupComponent available,
      BackupComponent component)
  {
    checkBox.IsEnabled = (available & component) != 0;
    if (!checkBox.IsEnabled)
    {
      checkBox.IsChecked = false;
    }
  }

  private void RestoreButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var selectedRows = _rows.Where(row => row.IsIncluded).ToArray();
    if (selectedRows.Length == 0)
    {
      ValidationText.Text =
          "復元するプロファイルを1件以上選んでください。";
      return;
    }

    if (selectedRows.Any(row => row.SelectedTarget is null))
    {
      ValidationText.Text =
          "選択したすべてのバックアップ元に復元先を指定してください。";
      return;
    }

    if (selectedRows
        .GroupBy(
            row => row.SelectedTarget!.Profile.ProfileId,
            StringComparer.Ordinal)
        .Any(group => group.Count() > 1))
    {
      ValidationText.Text =
          "1つの復元先へ複数のバックアップ元は割り当てられません。";
      return;
    }

    var components = ReadSelectedComponents();
    if (components == BackupComponent.None)
    {
      ValidationText.Text = "復元する内容を1つ以上選んでください。";
      return;
    }

    SelectedMode = SameEnvironmentRadioButton.IsChecked is true
        ? ProfileRestoreMode.SameEnvironment
        : ProfileRestoreMode.Migration;
    AllowFullRestoreAcrossEnvironment =
        !_environmentMatched
        && SelectedMode == ProfileRestoreMode.SameEnvironment;
    if (AllowFullRestoreAcrossEnvironment)
    {
      if (EnvironmentAcknowledgementCheckBox.IsChecked is not true)
      {
        ValidationText.Text =
            "環境ID不一致の注意を確認してください。";
        return;
      }

      var finalAnswer = MessageBox.Show(
          this,
          "別環境へ同一環境モードで復元します。端末依存データは動作しない可能性があります。本当に続けますか？",
          "最終確認",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning);
      if (finalAnswer != MessageBoxResult.Yes)
      {
        return;
      }
    }

    Mappings = selectedRows.Select(row =>
        new ProfileRestoreMapping(
            row.BackupProfileId,
            row.SelectedTarget!.Profile,
            components)).ToArray();
    DialogResult = true;
  }

  private void CancelButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DialogResult = false;
  }

  private void RestoreMode_Changed(
      object sender,
      RoutedEventArgs e)
  {
    if (IsInitialized)
    {
      UpdateEnvironmentWarning();
    }
  }

  private void UpdateEnvironmentWarning()
  {
    EnvironmentWarningBorder.Visibility =
        !_environmentMatched
        && SameEnvironmentRadioButton.IsChecked is true
            ? Visibility.Visible
            : Visibility.Collapsed;
  }

  private void FullProfileCheckBox_Changed(
      object sender,
      RoutedEventArgs e)
  {
    var full = FullProfileCheckBox.IsChecked is true;
    SettingsCheckBox.IsEnabled = !full;
    BookmarksCheckBox.IsEnabled = !full;
    HistoryCheckBox.IsEnabled = !full;
    ExtensionsCheckBox.IsEnabled = !full;
    SiteDataCheckBox.IsEnabled = !full;
    SessionsCheckBox.IsEnabled = !full;
  }

  private BackupComponent ReadSelectedComponents()
  {
    var components = FullProfileCheckBox.IsChecked is true
        ? BackupComponent.FullProfile
        : BackupComponent.None;
    if (FullProfileCheckBox.IsChecked is not true)
    {
      components |= SettingsCheckBox.IsChecked is true
          ? BackupComponent.Settings
          : BackupComponent.None;
      components |= BookmarksCheckBox.IsChecked is true
          ? BackupComponent.Bookmarks
          : BackupComponent.None;
      components |= HistoryCheckBox.IsChecked is true
          ? BackupComponent.History
          : BackupComponent.None;
      components |= ExtensionsCheckBox.IsChecked is true
          ? BackupComponent.Extensions
          : BackupComponent.None;
      components |= SiteDataCheckBox.IsChecked is true
          ? BackupComponent.CookiesAndSiteData
          : BackupComponent.None;
      components |= SessionsCheckBox.IsChecked is true
          ? BackupComponent.Sessions
          : BackupComponent.None;
    }

    components |= PasswordCsvCheckBox.IsChecked is true
        ? BackupComponent.PasswordCsv
        : BackupComponent.None;
    return components;
  }

  private static string GetBrowserDisplayName(string browserId)
  {
    return browserId switch
    {
      "chrome" => "Google Chrome",
      "edge" => "Microsoft Edge",
      "firefox" => "Mozilla Firefox",
      _ => browserId,
    };
  }

  private static string BuildTargetDisplayLabel(BrowserProfile profile)
  {
    return profile.WindowsUser is null
        ? profile.DisplayName
        : UiText.IsEnglish
            ? $"{profile.DisplayName} (Windows: "
                + $"{profile.WindowsUser.DisplayName})"
            : $"{profile.DisplayName}（Windows: "
                + $"{profile.WindowsUser.DisplayName}）";
  }

  private sealed record RestoreTargetOption(
      BrowserProfile Profile,
      string DisplayLabel);

  private sealed class RestoreMappingRow :
      INotifyPropertyChanged
  {
    private bool _isIncluded;
    private RestoreTargetOption? _selectedTarget;

    public RestoreMappingRow(
        string backupProfileId,
        string sourceLabel,
        IReadOnlyList<RestoreTargetOption> targets)
    {
      BackupProfileId = backupProfileId;
      SourceLabel = sourceLabel;
      Targets = targets;
      _isIncluded = targets.Count > 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BackupProfileId { get; }

    public string SourceLabel { get; }

    public IReadOnlyList<RestoreTargetOption> Targets { get; }

    public bool IsIncluded
    {
      get => _isIncluded;
      set
      {
        if (_isIncluded == value)
        {
          return;
        }

        _isIncluded = value;
        OnPropertyChanged();
      }
    }

    public RestoreTargetOption? SelectedTarget
    {
      get => _selectedTarget;
      set
      {
        if (ReferenceEquals(_selectedTarget, value))
        {
          return;
        }

        _selectedTarget = value;
        OnPropertyChanged();
      }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
      PropertyChanged?.Invoke(
          this,
          new PropertyChangedEventArgs(propertyName));
    }
  }
}
