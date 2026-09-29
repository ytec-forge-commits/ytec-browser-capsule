using System.Windows;
using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Domain.Settings;
using Ytec.BrowserCapsule.Infrastructure.ApplicationLifecycle;
using Ytec.BrowserCapsule.Infrastructure.Settings;

namespace Ytec.BrowserCapsule.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application, IDisposable
{
  private readonly SingleInstanceGuard _singleInstanceGuard =
      new(@"Local\YtecBrowserCapsule-4f83ee94");

  protected override void OnStartup(StartupEventArgs e)
  {
    AppPreferences preferences;
    try
    {
      preferences = new JsonAppPreferencesStore()
          .LoadAsync(CancellationToken.None)
          .GetAwaiter()
          .GetResult();
    }
    catch (Exception)
    {
      preferences = AppPreferences.Default;
    }

    var manualScreenshotMode = e.Args.Any(argument =>
        argument.StartsWith(
            "--manual-screenshot",
            StringComparison.Ordinal));
    var language = manualScreenshotMode
        ? e.Args.FirstOrDefault(argument => argument.StartsWith(
            "--language=",
            StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant() switch
        {
          "--language=en" => AppLanguage.English,
          "--language=ja" => AppLanguage.Japanese,
          _ => preferences.Language,
        }
        : preferences.Language;
    UiText.Initialize(language);

    if (!_singleInstanceGuard.TryAcquire())
    {
      ShutdownMode = ShutdownMode.OnExplicitShutdown;
      MessageBox.Show(
          "Y-TEC Browser Capsuleはすでに開いています。",
          "すでに起動しています",
          MessageBoxButton.OK,
          MessageBoxImage.Information);
      Shutdown();
      return;
    }

    base.OnStartup(e);
  }

  protected override void OnExit(ExitEventArgs e)
  {
    Dispose();
    base.OnExit(e);
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _singleInstanceGuard.Dispose();
    GC.SuppressFinalize(this);
  }
}
