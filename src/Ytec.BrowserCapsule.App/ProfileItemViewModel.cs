using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Application.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// 選択可能な1プロファイルの表示状態です。
/// </summary>
public sealed class ProfileItemViewModel : INotifyPropertyChanged
{
  private bool _isSelected = true;

  public ProfileItemViewModel(BrowserProfileOverview overview)
  {
    ArgumentNullException.ThrowIfNull(overview);
    Overview = overview;
  }

  public event PropertyChangedEventHandler? PropertyChanged;

  public event EventHandler? SelectionChanged;

  public BrowserProfileOverview Overview { get; }

  public string DisplayName => Overview.Profile.DisplayName;

  public string DirectoryName => Overview.Profile.ProfileDirectoryName;

  public long SizeBytes => Overview.Size.Bytes;

  public string SizeText => FormatBytes(Overview.Size.Bytes);

  public string FileCountText => UiText.F(
      "{0:N0}ファイル",
      Overview.Size.FileCount);

  public string SizeNote => Overview.Size.IsComplete
      ? UiText.T("推定容量")
      : UiText.T("読み取れない項目を除く推定容量");

  public string DirectoryText => UiText.F(
      "内部名: {0}",
      DirectoryName);

  public Visibility DefaultBadgeVisibility => Overview.Profile.IsDefault
      ? Visibility.Visible
      : Visibility.Collapsed;

  public Visibility FallbackBadgeVisibility =>
      Overview.Profile.Confidence is ProfileDiscoveryConfidence.DirectoryFallback
          ? Visibility.Visible
          : Visibility.Collapsed;

  public Visibility RunningBadgeVisibility => Overview.IsBrowserRunning
      ? Visibility.Visible
      : Visibility.Collapsed;

  public string WindowsUserText =>
      $"Windows: {Overview.Profile.WindowsUser?.DisplayName}";

  public Visibility WindowsUserBadgeVisibility =>
      Overview.Profile.WindowsUser?.IsCurrentUser is false
          ? Visibility.Visible
          : Visibility.Collapsed;

  public bool IsSelected
  {
    get => _isSelected;
    set
    {
      if (_isSelected == value)
      {
        return;
      }

      _isSelected = value;
      OnPropertyChanged();
      SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
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

  private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
  {
    PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs(propertyName));
  }
}
