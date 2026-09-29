using System.Diagnostics;
using System.IO;
using System.Windows;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Application.UserErrors;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.App;

public sealed record PasswordCsvImportAssistantItem(
    PasswordCsvImportFile Csv,
    BrowserProfile TargetProfile);

public partial class PasswordCsvImportAssistantDialog : Window
{
  private readonly IReadOnlyList<PasswordCsvImportAssistantItem> _items;
  private readonly IBrowserPasswordPageLauncher _pageLauncher;
  private int _currentIndex;

  public PasswordCsvImportAssistantDialog(
      IReadOnlyList<PasswordCsvImportAssistantItem> items,
      IBrowserPasswordPageLauncher pageLauncher)
  {
    ArgumentNullException.ThrowIfNull(items);
    ArgumentNullException.ThrowIfNull(pageLauncher);
    _items = items;
    _pageLauncher = pageLauncher;
    InitializeComponent();
    ShowCurrentItem();
    UiText.Apply(this);
  }

  public IReadOnlyList<string> ResidualPlaintextPaths { get; private set; }
      = [];

  private async void OpenBrowserButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    try
    {
      await _pageLauncher.OpenImportPageAsync(
          Current.TargetProfile,
          CancellationToken.None);
    }
    catch (Exception exception)
    {
      var error = UserFacingErrorMapper.Map(
          exception,
          UserOperation.PasswordCsv);
      MessageBox.Show(
          this,
          $"{error.Code}\n\n標準画面を開けませんでした。対象プロファイルのパスワード管理画面を手動で開いてください。",
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
    startInfo.ArgumentList.Add(
        $"/select,{Current.Csv.AbsolutePath}");
    _ = Process.Start(startInfo);
  }

  private void CopyPathButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    var folderPath = Path.GetDirectoryName(
        Current.Csv.AbsolutePath);
    if (!string.IsNullOrWhiteSpace(folderPath))
    {
      Clipboard.SetText(folderPath);
      StatusText.Text =
          "一時CSVがあるフォルダーのパスをコピーしました。";
    }
  }

  private void ImportedButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DeleteCurrentAndMoveNext();
  }

  private void SkipButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DeleteCurrentAndMoveNext();
  }

  private void CancelButton_Click(
      object sender,
      RoutedEventArgs e)
  {
    DialogResult = false;
  }

  private void DeleteCurrentAndMoveNext()
  {
    try
    {
      if (File.Exists(Current.Csv.AbsolutePath))
      {
        File.Delete(Current.Csv.AbsolutePath);
      }
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException)
    {
      ResidualPlaintextPaths =
      [
        .. ResidualPlaintextPaths,
        Current.Csv.AbsolutePath,
      ];
      MessageBox.Show(
          this,
          $"一時CSVを削除できませんでした。手動で削除してください。\n\n{Current.Csv.AbsolutePath}",
          "平文CSVが残っています",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }

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
        $"{_currentIndex + 1} / {_items.Count} — プロファイルごとに1件ずつ公式インポートします。";
    BrowserNameText.Text = item.Csv.BrowserId switch
    {
      "chrome" => "Google Chrome",
      "edge" => "Microsoft Edge",
      "firefox" => "Mozilla Firefox",
      _ => item.Csv.BrowserId,
    };
    ProfileNameText.Text = item.TargetProfile.DisplayName;
    CsvPathTextBox.Text = item.Csv.AbsolutePath;
    RecordCountText.Text =
        $"CSVのレコード数: {item.Csv.RecordCount:N0}件（値は表示しません）";
    StatusText.Text =
        "インポート後に完了ボタンを押すと、一時CSVを削除します。";
  }

  private PasswordCsvImportAssistantItem Current => _items[_currentIndex];
}
