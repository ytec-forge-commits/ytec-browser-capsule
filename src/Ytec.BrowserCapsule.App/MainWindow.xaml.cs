using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Application.BrowserDiscovery;
using Ytec.BrowserCapsule.Application.ProfileBackups;
using Ytec.BrowserCapsule.Application.ProfileRestores;
using Ytec.BrowserCapsule.Application.UserErrors;
using Ytec.BrowserCapsule.Browsers.Chrome.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Edge.BrowserDiscovery;
using Ytec.BrowserCapsule.Browsers.Firefox.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;
using Ytec.BrowserCapsule.Domain.Settings;
using Ytec.BrowserCapsule.Infrastructure.BackupContainers;
using Ytec.BrowserCapsule.Infrastructure.BrowserDiscovery;
using Ytec.BrowserCapsule.Infrastructure.PasswordCsv;
using Ytec.BrowserCapsule.Infrastructure.ProfileBackups;
using Ytec.BrowserCapsule.Infrastructure.ProfileRestores;
using Ytec.BrowserCapsule.Infrastructure.Settings;
using Ytec.BrowserCapsule.Infrastructure.WindowsUserProfiles;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// 検出したブラウザープロファイルを選択するメインウィンドウです。
/// </summary>
[SupportedOSPlatform("windows")]
public partial class MainWindow : Window, IDisposable
{
  private BrowserDiscoveryService _discoveryService = null!;
  private IReadOnlyList<IBrowserProfileSource> _sources = [];
  private readonly WindowsBrowserProcessInspector _processInspector = new();
  private readonly WindowsUserProfileCatalog _windowsUserProfileCatalog =
      new();
  private readonly List<ProfileItemViewModel> _profileItems = [];
  private readonly SecurePasswordCsvStagingFactory
      _passwordCsvStagingFactory = new();
  private readonly JsonAppPreferencesStore _preferencesStore = new();
  private WindowsBrowserPasswordPageLauncher
      _passwordPageLauncher = null!;
  private AppPreferences _preferences = AppPreferences.Default;
  private CancellationTokenSource? _discoveryCancellation;
  private CancellationTokenSource? _backupCancellation;
  private bool _isDiscoveryBusy;
  private bool _isBackupBusy;
  private int _readableWindowsUserCount;
  private int _skippedWindowsUserCount;

  public MainWindow()
  {
    InitializeComponent();
    ConfigureDiscoverySources(includeOtherWindowsUsers: false);

    Loaded += MainWindow_Loaded;
    Closing += MainWindow_Closing;
    UiText.Apply(this);
  }

  private async void MainWindow_Loaded(
      object sender,
      RoutedEventArgs e)
  {
    var commandLineArguments = Environment.GetCommandLineArgs();
    var manualScreenshotMode = commandLineArguments.Any(argument =>
        argument.StartsWith(
            "--manual-screenshot",
            StringComparison.Ordinal));
    if (manualScreenshotMode)
    {
      var snapshot = CreateManualScreenshotSnapshot();
      ApplySnapshot(snapshot);
      SetBusyState(isBusy: false);
      StatusDetailText.Text =
          "操作マニュアル用の合成プロファイルを表示しています。実データは読み取っていません。";
      if (commandLineArguments.Contains(
          "--manual-screenshot-options",
          StringComparer.Ordinal))
      {
        _ = new BackupOptionsDialog
        {
          Owner = this,
        }.ShowDialog();
      }
      else if (commandLineArguments.Contains(
          "--manual-screenshot-export",
          StringComparer.Ordinal))
      {
        await using var assistant = new PasswordCsvAssistantDialog(
            [snapshot.Profiles[0].Profile],
            new ManualScreenshotPasswordCsvStagingFactory(),
            _passwordPageLauncher)
        {
          Owner = this,
        };
        await assistant.InitializeAsync(CancellationToken.None);
        _ = assistant.ShowDialog();
      }
      else if (commandLineArguments.Contains(
          "--manual-screenshot-import",
          StringComparer.Ordinal))
      {
        const string fakeCsvPath =
            @"C:\Users\...\AppData\Local\Y-TEC\BrowserCapsule\Staging\example\chrome-passwords-01.csv";
        _ = new PasswordCsvImportAssistantDialog(
            [
              new PasswordCsvImportAssistantItem(
                  new PasswordCsvImportFile(
                      "manual-chrome-0",
                      "chrome",
                      fakeCsvPath,
                      RecordCount: 12),
                  snapshot.Profiles[0].Profile),
            ],
            _passwordPageLauncher)
        {
          Owner = this,
        }.ShowDialog();
      }

      return;
    }

    try
    {
      _preferences = await _preferencesStore.LoadAsync(
          CancellationToken.None);
    }
    catch (Exception)
    {
      StatusDetailText.Text =
          "設定を読み込めなかったため、既定値で起動しました。";
    }

    try
    {
      await _passwordCsvStagingFactory.CleanupStaleSessionsAsync(
          CancellationToken.None);
    }
    catch (Exception)
    {
      StatusDetailText.Text =
          "前回のCSV一時領域を清掃できませんでした。CSV補助を使う前に再起動してください。";
    }

    await RefreshProfilesAsync();
  }

  private async void RefreshButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    await RefreshProfilesAsync();
  }

  private void ManualButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var manualPath = Path.Combine(
        AppContext.BaseDirectory,
        UiText.ManualFileName);
    if (!File.Exists(manualPath))
    {
      MessageBox.Show(
          this,
          "操作マニュアルが見つかりません。"
              + "公式の配布ZIPを空のフォルダーへすべて展開し直してください。",
          "操作マニュアルを開けません",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      return;
    }

    try
    {
      Process.Start(new ProcessStartInfo
      {
        FileName = manualPath,
        UseShellExecute = true,
      });
    }
    catch (Exception)
    {
      MessageBox.Show(
          this,
          "操作マニュアルを開けませんでした。"
              + "PDFを開けるアプリが設定されているか確認してください。",
          "操作マニュアルを開けません",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }
  }

  private async void IncludeOtherWindowsUsers_Changed(
      object sender,
      RoutedEventArgs e)
  {
    if (IsLoaded)
    {
      await RefreshProfilesAsync();
    }
  }

  private void AboutButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var dialog = new AboutDialog
    {
      Owner = this,
    };
    dialog.ShowDialog();
  }

  private async void SettingsButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var dialog = new SettingsDialog(_preferences)
    {
      Owner = this,
    };
    if (dialog.ShowDialog() is not true
        || dialog.SelectedPreferences is not { } preferences)
    {
      return;
    }

    try
    {
      var languageChanged = preferences.Language != _preferences.Language;
      await _preferencesStore.SaveAsync(
          preferences,
          CancellationToken.None);
      _preferences = preferences;
      StatusText.Text = "設定を保存しました";
      StatusDetailText.Text = preferences.DefaultBackupDirectory is null
          ? "バックアップ先は保存時に選択します。"
          : "次回から指定したローカルフォルダーを最初に表示します。";
      if (languageChanged)
      {
        MessageBox.Show(
            this,
            "表示言語は、アプリを再起動すると切り替わります。",
            "再起動後に言語を変更します",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
      }
    }
    catch (Exception)
    {
      MessageBox.Show(
          this,
          "設定を保存できませんでした。フォルダーの書き込み権限を確認してください。",
          "設定の保存に失敗しました",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }
  }

  private void SelectAllButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    foreach (var profile in _profileItems)
    {
      profile.IsSelected = true;
    }
  }

  private void ClearSelectionButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    foreach (var profile in _profileItems)
    {
      profile.IsSelected = false;
    }
  }

  private async void CreateBackupButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var selectedItems = _profileItems
        .Where(item => item.IsSelected)
        .ToArray();
    if (selectedItems.Length == 0)
    {
      return;
    }

    var saveDialog = new SaveFileDialog
    {
      AddExtension = true,
      CheckPathExists = true,
      DefaultExt = ".bvb",
      FileName =
          $"Y-TEC-Browser-Capsule-{DateTime.Now:yyyyMMdd-HHmm}.bvb",
      Filter = UiText.T(
          "Y-TEC Browser Capsule バックアップ (*.bvb)|*.bvb"),
      OverwritePrompt = true,
      Title = UiText.T("暗号化バックアップの保存先"),
    };
    if (Directory.Exists(_preferences.DefaultBackupDirectory))
    {
      saveDialog.InitialDirectory =
          _preferences.DefaultBackupDirectory;
    }

    if (saveDialog.ShowDialog(this) is not true)
    {
      return;
    }

    var optionsDialog = new BackupOptionsDialog
    {
      Owner = this,
    };
    if (optionsDialog.ShowDialog() is not true)
    {
      return;
    }

    var recoveryKeyDialog = new SaveFileDialog
    {
      AddExtension = true,
      CheckPathExists = true,
      DefaultExt = ".ybckey",
      FileName =
          Path.GetFileNameWithoutExtension(saveDialog.FileName)
          + ".ybckey",
      Filter = UiText.T(
          "Y-TEC Browser Capsule 復元キー (*.ybckey)|*.ybckey"),
      InitialDirectory =
          Path.GetDirectoryName(saveDialog.FileName),
      OverwritePrompt = true,
      Title = UiText.T("復元キーファイルの保存先"),
    };
    if (recoveryKeyDialog.ShowDialog(this) is not true)
    {
      return;
    }

    if (string.Equals(
        Path.GetDirectoryName(saveDialog.FileName),
        Path.GetDirectoryName(recoveryKeyDialog.FileName),
        StringComparison.OrdinalIgnoreCase)
        && MessageBox.Show(
            this,
            "バックアップと復元キーが同じフォルダーです。\n\n"
                + "別PCへの移動は簡単ですが、フォルダーごと盗まれた場合の保護は弱くなります。"
                + "この場所へ作成しますか？",
            "復元キーの保管場所",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) is not MessageBoxResult.Yes)
    {
      return;
    }

    PasswordCsvAssistantDialog? passwordAssistant = null;
    var setBusy = false;

    try
    {
      IReadOnlyList<ProfilePasswordCsvSelection> passwordCsvSelections = [];
      if ((optionsDialog.SelectedComponents
          & BackupComponent.PasswordCsv) != 0)
      {
        var passwordCsvItems = selectedItems
            .Where(item =>
                item.Overview.Profile.WindowsUser?.IsCurrentUser
                    is not false)
            .ToArray();
        if (passwordCsvItems.Length != selectedItems.Length)
        {
          MessageBox.Show(
              this,
              "保存パスワードCSVは、現在ログイン中のWindowsユーザー分だけ公式画面から取得できます。ほかのWindowsユーザー分は、そのユーザーでサインインして別途バックアップしてください。",
              "保存パスワードCSVの対象",
              MessageBoxButton.OK,
              MessageBoxImage.Information);
        }

        if (passwordCsvItems.Length > 0)
        {
          passwordAssistant = new PasswordCsvAssistantDialog(
              passwordCsvItems
                  .Select(item => item.Overview.Profile)
                  .ToArray(),
              _passwordCsvStagingFactory,
              _passwordPageLauncher)
          {
            Owner = this,
          };
          await passwordAssistant.InitializeAsync(
              CancellationToken.None);
          if (passwordAssistant.ShowDialog() is not true)
          {
            return;
          }

          passwordCsvSelections =
              passwordAssistant.PasswordCsvSelections;
        }
      }

      var capturedProfileIds = passwordCsvSelections
          .Select(selection => selection.SourceProfileId)
          .ToHashSet(StringComparer.Ordinal);
      var profileSelections = selectedItems
          .Select(item =>
          {
            var components = optionsDialog.SelectedComponents;
            if ((components & BackupComponent.PasswordCsv) != 0
                && !capturedProfileIds.Contains(
                    item.Overview.Profile.ProfileId))
            {
              components &= ~BackupComponent.PasswordCsv;
            }

            return new ProfileBackupSelection(
                item.Overview.Profile,
                components);
          })
          .Where(selection =>
              selection.Components != BackupComponent.None)
          .ToArray();
      if (profileSelections.Length == 0)
      {
        MessageBox.Show(
            this,
            "バックアップへ含める内容がありません。",
            "バックアップを中止しました",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
      }

      var includesProfileData = profileSelections.Any(selection =>
          (selection.Components & ~BackupComponent.PasswordCsv)
          != BackupComponent.None);
      if (includesProfileData
          && !await EnsureBrowsersClosedWithHelpAsync(
              profileSelections.Select(selection =>
                  selection.Profile).ToArray(),
              CancellationToken.None))
      {
        StatusText.Text = "バックアップを中止しました";
        StatusDetailText.Text =
            "対象ブラウザーが実行中です。ブラウザーの内容は変更していません。";
        return;
      }

      _backupCancellation?.Dispose();
      _backupCancellation = new CancellationTokenSource();
      var cancellationToken = _backupCancellation.Token;
      SetBackupState(isBusy: true);
      setBusy = true;
      StatusText.Text = "暗号設定をこのPC向けに調整しています…";
      StatusDetailText.Text =
          "バックアップ専用の復元キーを安全な乱数から生成します。";
      var securityOptions = await Task.Run(
          BvbContainerSecurityOptions.CreateCalibrated,
          cancellationToken);
      var backupService = new ProfileBackupService(
          _sources,
          _processInspector,
          new WindowsEnvironmentFingerprintProvider(),
          new BvbBackupContainerService(securityOptions));
      var progress = new Progress<ProfileBackupProgress>(
          UpdateBackupProgress);
      var request = new ProfileBackupRequest(
          saveDialog.FileName,
          profileSelections,
          AppVersion:
              typeof(MainWindow).Assembly.GetName().Version?.ToString(3)
              ?? "1.2.1",
          passwordCsvSelections);

      var result = await backupService.CreateAsync(
          request,
          recoveryKeyDialog.FileName,
          progress,
          cancellationToken);
      StatusText.Text = "バックアップが完成しました";
      StatusDetailText.Text =
          $"{result.Container.Verification.EntryCount:N0}ファイルを暗号化して再検証しました。";
      MessageBox.Show(
          this,
          "暗号化バックアップと復元キーを作成しました。\n\n"
              + $"バックアップ:\n{result.Container.FinalPath}\n\n"
              + $"復元キー:\n{result.Container.RecoveryKeyPath}\n\n"
              + "別PCへ移すときは両方が必要です。",
          "バックアップ完了",
          MessageBoxButton.OK,
          result.Warnings.Count == 0
              ? MessageBoxImage.Information
              : MessageBoxImage.Warning);
    }
    catch (OperationCanceledException) when (
        _backupCancellation?.IsCancellationRequested is true)
    {
      StatusText.Text = "バックアップを中止しました";
      StatusDetailText.Text =
          "完成ファイルは作成していません。途中ファイルの削除を試みました。";
    }
    catch (BrowserMustBeClosedException exception)
    {
      StatusText.Text = "ブラウザーを終了してください";
      ShowUserError(
          exception,
          UserOperation.Backup,
          "バックアップを開始できません",
          MessageBoxImage.Information);
    }
    catch (Exception exception) when (
        exception is BackupPlanException
        or BackupContainerException
        or IOException
        or UnauthorizedAccessException)
    {
      StatusText.Text = "バックアップを作成できませんでした";
      ShowUserError(
          exception,
          UserOperation.Backup,
          "バックアップ失敗",
          MessageBoxImage.Error);
    }
    catch (Exception exception)
    {
      StatusText.Text = "バックアップを作成できませんでした";
      ShowUserError(
          exception,
          UserOperation.Backup,
          "バックアップ失敗",
          MessageBoxImage.Error);
    }
    finally
    {
      if (passwordAssistant is not null)
      {
        await passwordAssistant.DisposeAsync();
      }

      _backupCancellation?.Dispose();
      _backupCancellation = null;
      if (setBusy)
      {
        SetBackupState(isBusy: false);
      }
    }
  }

  private async void VerifyBackupButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var openDialog = CreateOpenBackupDialog("検証するバックアップ");
    if (openDialog.ShowDialog(this) is not true)
    {
      return;
    }

    var service = CreateReadContainerService();
    using var selectedCredential = await SelectCredentialAsync(
        service,
        openDialog.FileName,
        "検証に使う復元キー");
    if (selectedCredential is null)
    {
      return;
    }

    _backupCancellation?.Dispose();
    _backupCancellation = new CancellationTokenSource();
    var cancellationToken = _backupCancellation.Token;
    SetBackupState(isBusy: true);
    try
    {
      StatusText.Text = "バックアップを完全検証しています…";
      StatusDetailText.Text =
          "暗号タグ、ファイルハッシュ、マニフェストを最後まで確認します。";
      var verification = await service.VerifyAsync(
          openDialog.FileName,
          selectedCredential.Credential,
          cancellationToken);
      StatusText.Text = "バックアップは正常です";
      StatusDetailText.Text =
          $"{verification.Manifest.Profiles.Count:N0}プロファイル、"
          + $"{verification.EntryCount:N0}ファイルを検証しました。";
      MessageBox.Show(
          this,
          "暗号化バックアップの完全性を確認しました。\n\n"
          + $"作成日時: {verification.Manifest.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm}\n"
          + $"プロファイル数: {verification.Manifest.Profiles.Count:N0}\n"
          + $"ファイル数: {verification.EntryCount:N0}",
          "検証完了",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
    }
    catch (OperationCanceledException) when (
        cancellationToken.IsCancellationRequested)
    {
      StatusText.Text = "検証を中止しました";
      StatusDetailText.Text = "バックアップは変更していません。";
    }
    catch (BackupContainerIntegrityException exception)
    {
      StatusText.Text = "バックアップを確認できません";
      ShowUserError(
          exception,
          UserOperation.Verification,
          "検証失敗",
          MessageBoxImage.Error);
    }
    catch (Exception exception)
    {
      StatusText.Text = "バックアップを確認できません";
      ShowUserError(
          exception,
          UserOperation.Verification,
          "検証失敗",
          MessageBoxImage.Error);
    }
    finally
    {
      _backupCancellation.Dispose();
      _backupCancellation = null;
      SetBackupState(isBusy: false);
    }
  }

  private async void RestoreBackupButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var openDialog = CreateOpenBackupDialog(
        "復元するバックアップ");
    if (openDialog.ShowDialog(this) is not true)
    {
      return;
    }

    using var selectedCredential = await SelectCredentialAsync(
        CreateReadContainerService(),
        openDialog.FileName,
        "復元に使う復元キー");
    if (selectedCredential is null)
    {
      return;
    }

    _backupCancellation?.Dispose();
    _backupCancellation = new CancellationTokenSource();
    var cancellationToken = _backupCancellation.Token;
    SetBackupState(isBusy: true);
    try
    {
      StatusText.Text = "復元前にバックアップを検証しています…";
      StatusDetailText.Text =
          "この時点ではブラウザープロファイルを変更しません。";
      var securityOptions = await Task.Run(
          BvbContainerSecurityOptions.CreateCalibrated,
          cancellationToken);
      var containerService =
          new BvbBackupContainerService(securityOptions);
      var fingerprintProvider =
          new WindowsEnvironmentFingerprintProvider();
      var backupService = new ProfileBackupService(
          _sources,
          _processInspector,
          fingerprintProvider,
          containerService);
      var restoreService = new ProfileRestoreService(
          _sources,
          _processInspector,
          fingerprintProvider,
          containerService,
          backupService,
          new SafeRestoreFileApplier());
      var verification = await restoreService.InspectAsync(
          openDialog.FileName,
          selectedCredential.Credential,
          cancellationToken);
      var environmentMatched = string.Equals(
          verification.Manifest.EnvironmentFingerprint,
          fingerprintProvider.GetCurrentFingerprint(),
          StringComparison.Ordinal);
      var mappingDialog = new RestoreMappingDialog(
          verification,
          _profileItems
              .Select(item => item.Overview.Profile)
              .ToArray(),
          environmentMatched)
      {
        Owner = this,
      };
      if (mappingDialog.ShowDialog() is not true)
      {
        StatusText.Text = "復元をキャンセルしました";
        StatusDetailText.Text =
            "ブラウザープロファイルは変更されていません。";
        return;
      }

      if (!await EnsureBrowsersClosedWithHelpAsync(
          mappingDialog.Mappings.Select(mapping =>
              mapping.TargetProfile).ToArray(),
          cancellationToken))
      {
        StatusText.Text = "復元を中止しました";
        StatusDetailText.Text =
            "対象ブラウザーが実行中です。プロファイルは変更していません。";
        return;
      }

      MessageBox.Show(
          this,
          "対象ブラウザーが終了していることを確認しました。復元前の状態を暗号化ロールバックへ保存してから復元します。",
          "復元前の安全確認",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      var rollbackDirectory = Path.Combine(
          Environment.GetFolderPath(
              Environment.SpecialFolder.LocalApplicationData),
          "Y-TEC",
          "BrowserCapsule",
          "Rollbacks");
      var appVersion =
          typeof(MainWindow).Assembly.GetName().Version?.ToString(3)
          ?? "1.2.1";
      var progress = new Progress<ProfileRestoreProgress>(
          UpdateRestoreProgress);
      var result = await restoreService.RestoreAsync(
          new ProfileRestoreRequest(
              openDialog.FileName,
              mappingDialog.Mappings,
              mappingDialog.SelectedMode,
              mappingDialog.AllowFullRestoreAcrossEnvironment,
              rollbackDirectory,
              appVersion),
          selectedCredential.Credential,
          progress,
          cancellationToken);

      if (mappingDialog.ImportPasswordCsv)
      {
        await RunPasswordCsvImportAssistantAsync(
            restoreService,
            openDialog.FileName,
            selectedCredential.Credential,
            mappingDialog.Mappings,
            cancellationToken);
      }

      StatusText.Text = "復元が完了しました";
      StatusDetailText.Text =
          result.RollbackPath is null
              ? "プロファイルへ直接書き込まず、CSV移行手順を完了しました。"
              : $"{result.RestoredFileCount:N0}ファイルを復元し、ロールバックを保持しています。";
      MessageBox.Show(
          this,
          "復元が完了しました。ブラウザーを起動して内容を確認してください。\n\n"
          + (result.RollbackPath is null
              ? "プロファイル本体の変更はありません。\n\n"
              : $"ロールバック:\n{result.RollbackPath}\n"
                + $"ロールバック復元キー:\n"
                + $"{result.RollbackRecoveryKeyPath}\n\n"
                + "ロールバックと復元キーも別々の場所へ保管してください。\n\n")
          + (result.Warnings.Count > 0
              ? string.Join(
                  "\n",
                  result.Warnings.Select(warning =>
                      $"・{warning.Message}"))
              : "警告はありません。"),
          "復元完了",
          MessageBoxButton.OK,
          result.Warnings.Count == 0
              ? MessageBoxImage.Information
              : MessageBoxImage.Warning);
    }
    catch (OperationCanceledException) when (
        cancellationToken.IsCancellationRequested)
    {
      StatusText.Text = "復元を中止しました";
      StatusDetailText.Text =
          "書き込み開始後の場合は自動ロールバックを試みています。";
    }
    catch (BrowserMustBeClosedException exception)
    {
      StatusText.Text = "ブラウザーを終了してください";
      ShowUserError(
          exception,
          UserOperation.Restore,
          "復元を開始できません",
          MessageBoxImage.Information);
    }
    catch (RestoreFailedRolledBackException exception)
    {
      StatusText.Text = "復元前の状態へ戻しました";
      ShowUserError(
          exception,
          UserOperation.Restore,
          "復元失敗・ロールバック済み",
          MessageBoxImage.Warning);
    }
    catch (RestoreFailedRollbackFailedException exception)
    {
      StatusText.Text = "復元とロールバックに失敗しました";
      ShowUserError(
          exception,
          UserOperation.Restore,
          "緊急停止",
          MessageBoxImage.Error);
    }
    catch (Exception exception) when (
        exception is RestorePlanException
        or BackupContainerIntegrityException
        or BackupContainerException
        or IOException
        or UnauthorizedAccessException)
    {
      StatusText.Text = "復元できませんでした";
      ShowUserError(
          exception,
          UserOperation.Restore,
          "復元失敗",
          MessageBoxImage.Error);
    }
    catch (Exception exception)
    {
      StatusText.Text = "復元できませんでした";
      ShowUserError(
          exception,
          UserOperation.Restore,
          "復元失敗",
          MessageBoxImage.Error);
    }
    finally
    {
      _backupCancellation.Dispose();
      _backupCancellation = null;
      SetBackupState(isBusy: false);
    }
  }

  private void CancelBackupButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    _backupCancellation?.Cancel();
    CancelBackupButton.IsEnabled = false;
    StatusDetailText.Text =
        "安全に中止しています。少しお待ちください。";
  }

  private void MainWindow_Closing(
      object? sender,
      CancelEventArgs e)
  {
    Dispose();
  }

  public void Dispose()
  {
    _discoveryCancellation?.Cancel();
    _discoveryCancellation?.Dispose();
    _discoveryCancellation = null;
    _backupCancellation?.Cancel();
    _backupCancellation?.Dispose();
    _backupCancellation = null;
    GC.SuppressFinalize(this);
  }

  private async Task<bool> EnsureBrowsersClosedWithHelpAsync(
      IReadOnlyList<BrowserProfile> profiles,
      CancellationToken cancellationToken)
  {
    var rules = new List<ProcessMatchRule>();
    foreach (var browserGroup in profiles.GroupBy(
        profile => profile.BrowserId,
        StringComparer.Ordinal))
    {
      var source = _sources.FirstOrDefault(candidate =>
          string.Equals(
              candidate.BrowserId,
              browserGroup.Key,
              StringComparison.Ordinal));
      if (source is null)
      {
        return false;
      }

      var installations = await source.DiscoverInstallationsAsync(
          cancellationToken);
      foreach (var installationId in browserGroup
          .Select(profile => profile.InstallationId)
          .Distinct(StringComparer.Ordinal))
      {
        var installation = installations.FirstOrDefault(candidate =>
            string.Equals(
                candidate.InstallationId,
                installationId,
                StringComparison.Ordinal));
        if (installation is null)
        {
          return false;
        }

        rules.AddRange(source.GetProcessMatchRules(installation));
      }
    }

    rules = rules
        .DistinctBy(rule =>
            $"{rule.ProcessName}\0"
            + string.Join(
                "\0",
                rule.ExpectedExecutablePaths.Order(
                    StringComparer.OrdinalIgnoreCase)))
        .ToList();
    if (!await _processInspector.IsRunningAsync(
        rules,
        cancellationToken))
    {
      return true;
    }

    var closeChoice = MessageBox.Show(
        this,
        "対象ブラウザーが実行中です。\n\n"
            + "未保存の入力を保存してから進めてください。"
            + "「はい」を押すと、このアプリから通常の終了を依頼します。",
        "ブラウザーを終了しますか？",
        MessageBoxButton.YesNo,
        MessageBoxImage.Information,
        MessageBoxResult.Yes);
    if (closeChoice is not MessageBoxResult.Yes)
    {
      return false;
    }

    await _processInspector.RequestCloseAsync(
        rules,
        TimeSpan.FromSeconds(6),
        cancellationToken);
    if (!await _processInspector.IsRunningAsync(
        rules,
        cancellationToken))
    {
      return true;
    }

    var terminateChoice = MessageBox.Show(
        this,
        "通常終了後もバックグラウンドプロセスが残っています。\n\n"
            + "「はい」を押すと、現在のWindowsセッションで実行中の"
            + "対象ブラウザーだけを強制終了します。"
            + "未保存の入力は失われる可能性があります。",
        "残っているブラウザーを強制終了しますか？",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No);
    if (terminateChoice is not MessageBoxResult.Yes)
    {
      return false;
    }

    await _processInspector.TerminateRemainingAsync(
        rules,
        TimeSpan.FromSeconds(6),
        cancellationToken);
    return !await _processInspector.IsRunningAsync(
        rules,
        cancellationToken);
  }

  private async Task RunPasswordCsvImportAssistantAsync(
      ProfileRestoreService restoreService,
      string containerPath,
      BackupContainerCredential credential,
      IReadOnlyList<ProfileRestoreMapping> mappings,
      CancellationToken cancellationToken)
  {
    var csvMappings = mappings
        .Where(mapping =>
            (mapping.Components & BackupComponent.PasswordCsv) != 0
            && mapping.TargetProfile.WindowsUser?.IsCurrentUser
                is not false)
        .ToArray();
    var skippedOtherUserCsvCount = mappings.Count(mapping =>
        (mapping.Components & BackupComponent.PasswordCsv) != 0
        && mapping.TargetProfile.WindowsUser?.IsCurrentUser is false);
    if (skippedOtherUserCsvCount > 0)
    {
      MessageBox.Show(
          this,
          "ほかのWindowsユーザーへの保存パスワードCSV取込は、このログイン状態では実行しません。対象ユーザーでサインインして公式インポートを行ってください。",
          "保存パスワードCSVの取込",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
    }

    if (csvMappings.Length == 0)
    {
      return;
    }

    await using var stagingSession =
        await _passwordCsvStagingFactory.CreateAsync(
            cancellationToken);
    var files = await restoreService.ExtractPasswordCsvForImportAsync(
        containerPath,
        credential,
        csvMappings
            .Select(mapping => mapping.BackupProfileId)
            .ToHashSet(StringComparer.Ordinal),
        stagingSession.StagingDirectory,
        cancellationToken);
    if (files.Count == 0)
    {
      return;
    }

    var mappingByBackupId = csvMappings.ToDictionary(
        mapping => mapping.BackupProfileId,
        StringComparer.Ordinal);
    var items = files.Select(file =>
        new PasswordCsvImportAssistantItem(
            file,
            mappingByBackupId[file.BackupProfileId]
                .TargetProfile))
        .ToArray();
    var assistant = new PasswordCsvImportAssistantDialog(
        items,
        _passwordPageLauncher)
    {
      Owner = this,
    };
    _ = assistant.ShowDialog();
    if (assistant.ResidualPlaintextPaths.Count > 0)
    {
      MessageBox.Show(
          this,
          "削除できなかった一時CSVがあります。表示されたパスを手動で削除してください。\n\n"
          + string.Join(
              "\n",
              assistant.ResidualPlaintextPaths),
          "平文CSVの削除が必要です",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }
  }

  private async Task<SelectedContainerCredential?>
      SelectCredentialAsync(
          BvbBackupContainerService service,
          string containerPath,
          string recoveryKeyDialogTitle)
  {
    BackupContainerHeaderInfo header;
    try
    {
      header = await service.ReadHeaderAsync(
          containerPath,
          CancellationToken.None);
    }
    catch (Exception exception)
    {
      ShowUserError(
          exception,
          UserOperation.Verification,
          "バックアップを開けません",
          MessageBoxImage.Error);
      return null;
    }

    if (header.CredentialKind
        == BackupContainerCredentialKind.RecoveryKeyFile)
    {
      var keyDialog = new OpenFileDialog
      {
        AddExtension = true,
        CheckFileExists = true,
        CheckPathExists = true,
        DefaultExt = ".ybckey",
        FileName =
            Path.GetFileNameWithoutExtension(containerPath)
            + ".ybckey",
        Filter = UiText.T(
            "Y-TEC Browser Capsule 復元キー (*.ybckey)|*.ybckey"),
        InitialDirectory = Path.GetDirectoryName(containerPath),
        Multiselect = false,
        Title = UiText.T(recoveryKeyDialogTitle),
      };
      if (keyDialog.ShowDialog(this) is not true)
      {
        return null;
      }

      return new SelectedContainerCredential(
          BackupContainerCredential.FromRecoveryKeyFile(
              keyDialog.FileName),
          legacyCharacters: null);
    }

    var passphraseDialog = new PassphrasePromptDialog
    {
      Owner = this,
    };
    if (passphraseDialog.ShowDialog() is not true
        || passphraseDialog.PassphraseCharacters is not { } characters)
    {
      return null;
    }

    return new SelectedContainerCredential(
        BackupContainerCredential.FromLegacyPassphrase(characters),
        characters);
  }

  private static OpenFileDialog CreateOpenBackupDialog(string title)
  {
    return new OpenFileDialog
    {
      AddExtension = true,
      CheckFileExists = true,
      CheckPathExists = true,
      DefaultExt = ".bvb",
      Filter = UiText.T(
          "Y-TEC Browser Capsule バックアップ (*.bvb)|*.bvb"),
      Multiselect = false,
      Title = UiText.T(title),
    };
  }

  private static BvbBackupContainerService CreateReadContainerService()
  {
    return new BvbBackupContainerService(
        new BvbContainerSecurityOptions(
            BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
            BvbContainerSecurityOptions.DefaultChunkSizeBytes,
            new Version(1, 1, 0)));
  }

  private sealed class SelectedContainerCredential : IDisposable
  {
    private char[]? _legacyCharacters;

    public SelectedContainerCredential(
        BackupContainerCredential credential,
        char[]? legacyCharacters)
    {
      Credential = credential;
      _legacyCharacters = legacyCharacters;
    }

    public BackupContainerCredential Credential { get; }

    public void Dispose()
    {
      if (_legacyCharacters is not null)
      {
        CryptographicOperations.ZeroMemory(
            MemoryMarshal.AsBytes(_legacyCharacters.AsSpan()));
        _legacyCharacters = null;
      }
    }
  }

  private sealed class ManualScreenshotPasswordCsvStagingFactory :
      IPasswordCsvStagingFactory
  {
    public Task CleanupStaleSessionsAsync(
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.CompletedTask;
    }

    public Task<IPasswordCsvStagingSession> CreateAsync(
        CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      return Task.FromResult<IPasswordCsvStagingSession>(
          new ManualScreenshotPasswordCsvStagingSession());
    }
  }

  private sealed class ManualScreenshotPasswordCsvStagingSession :
      IPasswordCsvStagingSession
  {
    public string StagingDirectory =>
        @"C:\Users\...\AppData\Local\Y-TEC\BrowserCapsule\Staging\example";

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }

    public async Task<string?> WaitForStableCsvAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
      await Task.Delay(timeout, cancellationToken);
      return null;
    }

    public Task<IProtectedPasswordCsv> ProtectAsync(
        string csvPath,
        bool allowEmpty,
        CancellationToken cancellationToken)
    {
      throw new NotSupportedException(
          "スクリーンショットモードではCSVを暗号化しません。");
    }
  }

  private void ShowUserError(
      Exception exception,
      UserOperation operation,
      string title,
      MessageBoxImage image)
  {
    var error = UserFacingErrorMapper.Map(exception, operation);
    StatusDetailText.Text = $"{error.Code} — {error.Message}";
    MessageBox.Show(
        this,
        $"{error.Code}\n\n{error.Message}\n\n対処方法\n{error.SuggestedAction}",
        title,
        MessageBoxButton.OK,
        image);
  }

  private async Task RefreshProfilesAsync()
  {
    _discoveryCancellation?.Cancel();
    _discoveryCancellation?.Dispose();
    _discoveryCancellation = new CancellationTokenSource();
    var cancellationToken = _discoveryCancellation.Token;

    SetBusyState(isBusy: true);

    try
    {
      ConfigureDiscoverySources(
          IncludeOtherWindowsUsersCheckBox.IsChecked is true);
      var snapshot = await _discoveryService.DiscoverAsync(cancellationToken);
      ApplySnapshot(snapshot);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // 再検出または終了によるキャンセルは画面へエラー表示しません。
    }
    catch (Exception)
    {
      _profileItems.Clear();
      BrowserGroupsItemsControl.ItemsSource = null;
      EmptyStateBorder.Visibility = Visibility.Visible;
      StatusText.Text = "ブラウザーを確認できませんでした";
      StatusDetailText.Text =
          "しばらく待ってから再検出してください。ブラウザーの内容は変更されていません。";
    }
    finally
    {
      if (!cancellationToken.IsCancellationRequested)
      {
        SetBusyState(isBusy: false);
      }
    }
  }

  private void ApplySnapshot(BrowserDiscoverySnapshot snapshot)
  {
    foreach (var oldItem in _profileItems)
    {
      oldItem.SelectionChanged -= ProfileItem_SelectionChanged;
    }

    _profileItems.Clear();
    _profileItems.AddRange(snapshot.Profiles.Select(
        overview => new ProfileItemViewModel(overview)));

    foreach (var item in _profileItems)
    {
      item.SelectionChanged += ProfileItem_SelectionChanged;
    }

    var groups = _profileItems
        .GroupBy(item => item.Overview.BrowserDisplayName)
        .Select(group => new BrowserGroupViewModel(
            group.Key,
            GetBrowserAccent(group.First().Overview.Profile.BrowserId),
            group.ToArray()))
        .ToArray();

    BrowserGroupsItemsControl.ItemsSource = groups;
    EmptyStateBorder.Visibility = groups.Length == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    StatusText.Text = groups.Length == 0
        ? "対象のプロファイルは0件です"
        : $"{groups.Length}ブラウザー、{_profileItems.Count}プロファイルを検出しました";

    var runningBrowserCount = _profileItems
        .Where(item => item.Overview.IsBrowserRunning)
        .Select(item => item.Overview.Profile.BrowserId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    var issueCount = snapshot.Issues.Count;

    StatusDetailText.Text = BuildStatusDetail(
        runningBrowserCount,
        issueCount,
        _readableWindowsUserCount,
        _skippedWindowsUserCount,
        IncludeOtherWindowsUsersCheckBox.IsChecked is true);
    UpdateSelectionSummary();
    _ = Dispatcher.BeginInvoke(
        new Action(() => UiText.Apply(this)));
  }

  private static BrowserDiscoverySnapshot
      CreateManualScreenshotSnapshot()
  {
    var definitions = new[]
    {
      (
        BrowserId: "chrome",
        BrowserName: "Google Chrome",
        InstallationId: "manual-chrome",
        Profiles: new[]
        {
          (ManualName("仕事用", "Work"), "Default",
              486L * 1024 * 1024, true),
          (ManualName("個人用", "Personal"), "Profile 1",
              712L * 1024 * 1024, false),
        }),
      (
        BrowserId: "edge",
        BrowserName: "Microsoft Edge",
        InstallationId: "manual-edge",
        Profiles: new[]
        {
          (ManualName("メイン", "Main"), "Default",
              328L * 1024 * 1024, true),
          (ManualName("検証用", "Testing"), "Profile 1",
              195L * 1024 * 1024, false),
        }),
      (
        BrowserId: "firefox",
        BrowserName: "Mozilla Firefox",
        InstallationId: "manual-firefox",
        Profiles: new[]
        {
          ("default-release", "abcd1234.default-release",
              264L * 1024 * 1024, true),
          (ManualName("サブ", "Secondary"), "efgh5678.sub",
              141L * 1024 * 1024, false),
        }),
    };

    var overviews = definitions.SelectMany(definition =>
        definition.Profiles.Select((profile, index) =>
            new BrowserProfileOverview(
                definition.BrowserName,
                new BrowserProfile(
                    definition.BrowserId,
                    definition.InstallationId,
                    $"{definition.InstallationId}-{index}",
                    profile.Item1,
                    profile.Item2,
                    Path.Combine(
                        @"C:\YBC-Manual",
                        definition.BrowserId,
                        profile.Item2),
                    Path.Combine(
                        @"C:\YBC-Manual",
                        definition.BrowserId),
                    profile.Item4,
                    LastUsedUtc: null,
                    BrowserChannel.Stable,
                    ProfileDiscoveryConfidence.Metadata),
                new ProfileSizeEstimate(
                    profile.Item3,
                    FileCount: 120 + index * 30,
                    SkippedEntryCount: 0,
                    IsComplete: true),
                IsBrowserRunning: false)))
        .ToArray();
    return new BrowserDiscoverySnapshot(
        overviews,
        Issues: [],
        DateTimeOffset.UtcNow);
  }

  private static string ManualName(
      string japanese,
      string english)
  {
    return UiText.IsEnglish ? english : japanese;
  }

  private void ProfileItem_SelectionChanged(object? sender, EventArgs e)
  {
    UpdateSelectionSummary();
  }

  private void UpdateSelectionSummary()
  {
    var selected = _profileItems.Where(item => item.IsSelected).ToArray();
    var selectedBytes = selected.Sum(item => item.SizeBytes);
    SelectionSummaryText.Text =
        $"{selected.Length}件を選択・合計 約{FormatBytes(selectedBytes)}";
    UpdateActionAvailability();
  }

  private void SetBusyState(bool isBusy)
  {
    _isDiscoveryBusy = isBusy;
    UpdateActionAvailability();
    BusyStateBorder.Visibility = isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    ProfilesScrollViewer.Visibility = isBusy
        ? Visibility.Hidden
        : Visibility.Visible;

    if (isBusy)
    {
      EmptyStateBorder.Visibility = Visibility.Collapsed;
      StatusText.Text = "ブラウザーを確認しています…";
      StatusDetailText.Text =
          "設定ファイルの読み取りと容量見積もりだけを行います。";
    }
  }

  private void SetBackupState(bool isBusy)
  {
    _isBackupBusy = isBusy;
    BrowserGroupsItemsControl.IsEnabled = !isBusy;
    BackupProgressBar.Visibility = isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    if (!isBusy)
    {
      BackupProgressBar.Value = 0;
    }

    CancelBackupButton.Visibility = isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    CancelBackupButton.IsEnabled = isBusy;
    UpdateActionAvailability();
  }

  private void UpdateActionAvailability()
  {
    var controlsEnabled = !_isDiscoveryBusy && !_isBackupBusy;
    SettingsButton.IsEnabled = controlsEnabled;
    AboutButton.IsEnabled = controlsEnabled;
    RefreshButton.IsEnabled = controlsEnabled;
    SelectAllButton.IsEnabled = controlsEnabled;
    ClearSelectionButton.IsEnabled = controlsEnabled;
    VerifyBackupButton.IsEnabled = controlsEnabled;
    RestoreBackupButton.IsEnabled =
        controlsEnabled && _profileItems.Count > 0;
    CreateBackupButton.IsEnabled =
        controlsEnabled && _profileItems.Any(item => item.IsSelected);
    IncludeOtherWindowsUsersCheckBox.IsEnabled = controlsEnabled;
  }

  private void UpdateBackupProgress(ProfileBackupProgress progress)
  {
    var percentage = progress.TotalBytes > 0
        ? Math.Clamp(
            progress.ProcessedBytes * 100d / progress.TotalBytes,
            0,
            100)
        : progress.Stage >= ProfileBackupStage.Verifying
            ? 100
            : 0;
    BackupProgressBar.Value = percentage;
    StatusText.Text = progress.Stage switch
    {
      ProfileBackupStage.Planning => "バックアップ内容を確認しています…",
      ProfileBackupStage.Writing => "暗号化バックアップを作成しています…",
      ProfileBackupStage.Verifying => "完成前の安全確認をしています…",
      ProfileBackupStage.Completed => "バックアップが完成しました",
      _ => "処理しています…",
    };
    StatusDetailText.Text = progress.Stage switch
    {
      ProfileBackupStage.Writing =>
          $"{progress.CompletedFiles:N0} / {progress.TotalFiles:N0}ファイル・{percentage:N0}%",
      ProfileBackupStage.Verifying =>
          "暗号タグ、ファイルハッシュ、マニフェストを再検証しています。",
      _ => "元のプロファイルは変更しません。",
    };
  }

  private void UpdateRestoreProgress(ProfileRestoreProgress progress)
  {
    BackupProgressBar.Value = progress.TotalFiles > 0
        ? Math.Clamp(
            progress.CompletedFiles * 100d / progress.TotalFiles,
            0,
            100)
        : 0;
    StatusText.Text = progress.Stage switch
    {
      ProfileRestoreStage.Inspecting => "バックアップを検証しています…",
      ProfileRestoreStage.CreatingRollback =>
          "復元前ロールバックを暗号化しています…",
      ProfileRestoreStage.Extracting =>
          "安全な一時領域へ検証展開しています…",
      ProfileRestoreStage.Applying => "プロファイルを復元しています…",
      ProfileRestoreStage.Verifying => "復元結果を確認しています…",
      ProfileRestoreStage.RollingBack =>
          "復元前の状態へ戻しています…",
      ProfileRestoreStage.Completed => "復元が完了しました",
      _ => "処理しています…",
    };
    StatusDetailText.Text = progress.TotalFiles > 0
        ? $"{progress.CompletedFiles:N0} / {progress.TotalFiles:N0}ファイル"
        : "ブラウザーを起動せず、そのままお待ちください。";
  }

  private static string BuildStatusDetail(
      int runningBrowserCount,
      int issueCount,
      int readableWindowsUserCount,
      int skippedWindowsUserCount,
      bool includesOtherWindowsUsers)
  {
    var parts = new List<string>
    {
      "検出結果は読み取り専用です。",
    };

    if (runningBrowserCount > 0)
    {
      parts.Add($"{runningBrowserCount}種類のブラウザーが実行中です。");
    }

    if (includesOtherWindowsUsers)
    {
      parts.Add(
          $"{readableWindowsUserCount}件のWindowsユーザー領域を確認しました。");
    }

    if (skippedWindowsUserCount > 0)
    {
      parts.Add(
          $"{skippedWindowsUserCount}件は権限を変えず読み飛ばしました。");
    }

    if (issueCount > 0)
    {
      parts.Add($"{issueCount}件は安全に読み飛ばしました。");
    }

    return string.Join(" ", parts);
  }

  private void ConfigureDiscoverySources(
      bool includeOtherWindowsUsers)
  {
    var result = _windowsUserProfileCatalog.Discover(
        includeOtherWindowsUsers);
    _readableWindowsUserCount = result.Profiles.Count;
    _skippedWindowsUserCount = result.SkippedProfileCount;
    _sources =
    [
      new ChromeBrowserProfileSource(result.Profiles),
      new EdgeBrowserProfileSource(result.Profiles),
      new FirefoxBrowserProfileSource(result.Profiles),
    ];
    _discoveryService = new BrowserDiscoveryService(
        _sources,
        new ProfileSizeEstimator(),
        _processInspector);
    _passwordPageLauncher =
        new WindowsBrowserPasswordPageLauncher(_sources);
  }

  private static string GetBrowserAccent(string browserId)
  {
    return browserId switch
    {
      "chrome" => "#E4A21A",
      "edge" => "#22A784",
      "firefox" => "#8D62D9",
      _ => "#5A78B8",
    };
  }

  private static string FormatBytes(long bytes)
  {
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    var value = (double)Math.Max(0, bytes);
    var unitIndex = 0;

    while (value >= 1024 && unitIndex < units.Length - 1)
    {
      value /= 1024;
      unitIndex++;
    }

    return unitIndex == 0
        ? $"{value:N0} {units[unitIndex]}"
        : $"{value:N1} {units[unitIndex]}";
  }
}
