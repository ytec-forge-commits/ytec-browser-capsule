using System.Diagnostics;
using System.Windows;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Application.UserErrors;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.App;

public partial class PasswordCsvAssistantDialog :
    Window,
    IAsyncDisposable
{
  private readonly IReadOnlyList<BrowserProfile> _profiles;
  private readonly IPasswordCsvStagingFactory _stagingFactory;
  private readonly IBrowserPasswordPageLauncher _pageLauncher;
  private readonly List<AssistantItem> _items = [];
  private CancellationTokenSource? _captureCancellation;
  private Task _captureTask = Task.CompletedTask;
  private int _currentIndex;

  public PasswordCsvAssistantDialog(
      IReadOnlyList<BrowserProfile> profiles,
      IPasswordCsvStagingFactory stagingFactory,
      IBrowserPasswordPageLauncher pageLauncher)
  {
    ArgumentNullException.ThrowIfNull(profiles);
    ArgumentNullException.ThrowIfNull(stagingFactory);
    ArgumentNullException.ThrowIfNull(pageLauncher);
    _profiles = profiles;
    _stagingFactory = stagingFactory;
    _pageLauncher = pageLauncher;
    InitializeComponent();
    UiText.Apply(this);
  }

  public IReadOnlyList<ProfilePasswordCsvSelection>
      PasswordCsvSelections => _items
          .Where(item => item.Capture is not null)
          .Select(item => new ProfilePasswordCsvSelection(
              item.Profile.ProfileId,
              item.Capture!))
          .ToArray();

  public async Task InitializeAsync(CancellationToken cancellationToken)
  {
    foreach (var profile in _profiles)
    {
      var session = await _stagingFactory.CreateAsync(
          cancellationToken);
      _items.Add(new AssistantItem(profile, session));
    }

    ShowCurrentItem();
  }

  public async ValueTask DisposeAsync()
  {
    await CancelCaptureAsync();
    foreach (var item in _items)
    {
      await item.Session.DisposeAsync();
    }

    _items.Clear();
    GC.SuppressFinalize(this);
  }

  private async void OpenBrowserButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    try
    {
      await _pageLauncher.OpenExportPageAsync(
          Current.Profile,
          CancellationToken.None);
      StatusText.Text =
          "CSVの保存を待っています。ブラウザー標準画面でエクスポートし、表示された専用フォルダーへ保存してください。";
      _captureTask = CaptureCurrentAsync(
          TimeSpan.FromMinutes(5),
          "CSVを自動検出しています。保存後、そのままお待ちください…");
      await _captureTask;
    }
    catch (OperationCanceledException)
        when (_captureCancellation?.IsCancellationRequested is true)
    {
      // 閉じる操作または再試行により待機を中止しました。
    }
    catch (Exception exception)
    {
      var error = UserFacingErrorMapper.Map(
          exception,
          UserOperation.PasswordCsv);
      MessageBox.Show(
          this,
          $"{error.Code}\n\n標準画面を開けませんでした。ブラウザーのパスワード管理画面を手動で開き、表示中の保存先へCSVを保存してください。",
          "ブラウザーを開けません",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }
  }

  private void OpenFolderButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "explorer.exe",
      UseShellExecute = false,
    };
    startInfo.ArgumentList.Add(Current.Session.StagingDirectory);
    _ = Process.Start(startInfo);
  }

  private void CopyPathButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    Clipboard.SetText(Current.Session.StagingDirectory);
    StatusText.Text = "保存先パスをクリップボードへコピーしました。";
  }

  private async void CaptureButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    _captureTask = CaptureCurrentAsync(
        TimeSpan.FromSeconds(30),
        "CSVの書き込み完了を確認しています。保存後、最大30秒待ちます…");
    await _captureTask;
  }

  private async Task CaptureCurrentAsync(
      TimeSpan waitTimeout,
      string waitingMessage)
  {
    _captureCancellation?.Cancel();
    _captureCancellation?.Dispose();
    var captureCancellation = new CancellationTokenSource();
    _captureCancellation = captureCancellation;
    var cancellationToken = captureCancellation.Token;
    SetCaptureBusy(isBusy: true);
    try
    {
      StatusText.Text = waitingMessage;
      var csvPath = await Current.Session.WaitForStableCsvAsync(
          waitTimeout,
          cancellationToken);
      if (csvPath is null)
      {
        StatusText.Text =
            "CSVを確認できませんでした。専用フォルダー直下へ保存してから再試行してください。";
        return;
      }

      IProtectedPasswordCsv capture;
      try
      {
        capture = await Current.Session.ProtectAsync(
            csvPath,
            allowEmpty: false,
            cancellationToken);
      }
      catch (PasswordCsvValidationException exception)
          when (exception.Message.Contains(
              "空のCSV",
              StringComparison.Ordinal))
      {
        var answer = MessageBox.Show(
            this,
            "CSVに保存パスワードがありません。空のCSVとしてバックアップへ含めますか？",
            "空のCSV",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
          return;
        }

        capture = await Current.Session.ProtectAsync(
            csvPath,
            allowEmpty: true,
            cancellationToken);
      }

      Current.Capture = capture;
      StatusText.Text = capture.ResidualPlaintextPath is null
          ? $"CSVを暗号化しました（{capture.Validation.RecordCount:N0}件）。平文は削除済みです。"
          : $"CSVは暗号化しましたが、平文を削除できませんでした: {capture.ResidualPlaintextPath}";
      ContinueButton.IsEnabled = true;
      CaptureButton.IsEnabled = false;
      SkipButton.IsEnabled = false;
      OpenBrowserButton.IsEnabled = false;
    }
    catch (OperationCanceledException)
        when (cancellationToken.IsCancellationRequested)
    {
      StatusText.Text = "CSVの待機を中止しました。";
    }
    catch (PasswordCsvValidationException exception)
    {
      var error = UserFacingErrorMapper.Map(
          exception,
          UserOperation.PasswordCsv);
      StatusText.Text =
          $"{error.Code} — {error.Message} {error.SuggestedAction}";
    }
    catch (Exception exception)
    {
      var error = UserFacingErrorMapper.Map(
          exception,
          UserOperation.PasswordCsv);
      StatusText.Text =
          $"{error.Code} — {error.Message} {error.SuggestedAction}";
    }
    finally
    {
      if (ReferenceEquals(_captureCancellation, captureCancellation))
      {
        _captureCancellation = null;
      }

      captureCancellation.Dispose();
      SetCaptureBusy(isBusy: false);
    }
  }

  private async void SkipButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var item = Current;
    SkipButton.IsEnabled = false;
    try
    {
      await CancelCaptureAsync();
      await item.Session.DisposeAsync();
      item.Capture = null;
      item.Skipped = true;
      MoveNextOrFinish();
    }
    catch (Exception exception)
    {
      var error = UserFacingErrorMapper.Map(
          exception,
          UserOperation.PasswordCsv);
      StatusText.Text =
          $"{error.Code} — スキップ前の一時データを整理できませんでした。"
          + $" {error.SuggestedAction}";
      SkipButton.IsEnabled = true;
    }
  }

  private void ContinueButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    MoveNextOrFinish();
  }

  private void CancelButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    _captureCancellation?.Cancel();
    DialogResult = false;
  }

  private void MoveNextOrFinish()
  {
    if (_currentIndex >= _items.Count - 1)
    {
      DialogResult = true;
      return;
    }

    _currentIndex++;
    ShowCurrentItem();
  }

  private void ShowCurrentItem()
  {
    var item = Current;
    StepText.Text =
        $"{_currentIndex + 1} / {_items.Count} — プロファイルごとにCSVを取り違えないよう、1件ずつ進めます。";
    BrowserNameText.Text = item.Profile.BrowserId switch
    {
      "chrome" => "Google Chrome",
      "edge" => "Microsoft Edge",
      "firefox" => "Mozilla Firefox",
      _ => item.Profile.BrowserId,
    };
    ProfileNameText.Text = item.Profile.DisplayName;
    StagingPathTextBox.Text = item.Session.StagingDirectory;
    StatusText.Text =
        "CSVを保存したら取り込みを開始してください。CSVの値は画面やログへ表示しません。";
    CaptureButton.IsEnabled = true;
    OpenBrowserButton.IsEnabled = true;
    SkipButton.IsEnabled = true;
    ContinueButton.IsEnabled = false;
    ContinueButton.Content = _currentIndex == _items.Count - 1
        ? "バックアップへ"
        : "次へ";
  }

  private void SetCaptureBusy(bool isBusy)
  {
    DetectionProgressBar.Visibility = isBusy
        ? Visibility.Visible
        : Visibility.Collapsed;
    CaptureButton.IsEnabled = !isBusy && Current.Capture is null;
    OpenBrowserButton.IsEnabled =
        !isBusy && Current.Capture is null;
    SkipButton.IsEnabled = Current.Capture is null;
    ContinueButton.IsEnabled =
        !isBusy && (Current.Capture is not null || Current.Skipped);
  }

  private async Task CancelCaptureAsync()
  {
    _captureCancellation?.Cancel();
    try
    {
      await _captureTask;
    }
    catch (OperationCanceledException)
    {
      // 待機中のスキップ、キャンセル、終了は正常な中止として扱います。
    }
  }

  private AssistantItem Current => _items[_currentIndex];

  private sealed class AssistantItem(
      BrowserProfile profile,
      IPasswordCsvStagingSession session)
  {
    public BrowserProfile Profile { get; } = profile;

    public IPasswordCsvStagingSession Session { get; } = session;

    public IProtectedPasswordCsv? Capture { get; set; }

    public bool Skipped { get; set; }
  }
}
