using System.Xml.Linq;

namespace Ytec.BrowserCapsule.IntegrationTests.Accessibility;

public sealed class XamlAccessibilityTests
{
  private static readonly XNamespace Presentation =
      "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

  [Fact]
  public void InteractiveControlsHaveAccessibleNames()
  {
    foreach (var file in GetAppXamlFiles())
    {
      var document = XDocument.Load(file);
      var controls = document.Descendants().Where(element =>
          element.Name == Presentation + "PasswordBox"
          || element.Name == Presentation + "TextBox"
          || element.Name == Presentation + "ProgressBar"
          || element.Name == Presentation + "ComboBox"
          || element.Name == Presentation + "Button"
          || element.Name == Presentation + "CheckBox");

      foreach (var control in controls)
      {
        var hasContent = !string.IsNullOrWhiteSpace(
            control.Attribute("Content")?.Value);
        var hasAutomationName = !string.IsNullOrWhiteSpace(
            control.Attribute("AutomationProperties.Name")?.Value);
        Assert.True(
            hasContent || hasAutomationName,
            $"{Path.GetFileName(file)} の {control.Name.LocalName} "
            + "にContentまたはAutomationProperties.Nameがありません。");
      }
    }
  }

  [Fact]
  public void WindowsDeclareKeyboardNavigationAndTitle()
  {
    foreach (var file in GetAppXamlFiles())
    {
      var document = XDocument.Load(file);
      var window = document.Root;
      if (window?.Name != Presentation + "Window")
      {
        continue;
      }

      Assert.False(
          string.IsNullOrWhiteSpace(window.Attribute("Title")?.Value),
          $"{Path.GetFileName(file)} にTitleがありません。");
      Assert.False(
          string.IsNullOrWhiteSpace(
              window.Attribute(
                  "KeyboardNavigation.TabNavigation")?.Value),
          $"{Path.GetFileName(file)} にTabNavigationがありません。");
    }
  }

  [Fact]
  public void ValidationAndStatusTextUsesLiveRegions()
  {
    XNamespace xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    foreach (var file in GetAppXamlFiles())
    {
      var document = XDocument.Load(file);
      var liveText = document.Descendants()
          .Where(element => element.Name == Presentation + "TextBlock")
          .Where(element =>
          {
            var name = element.Attribute(xaml + "Name")?.Value;
            return name is "ValidationText" or "StatusText";
          });

      foreach (var text in liveText)
      {
        Assert.False(
            string.IsNullOrWhiteSpace(
                text.Attribute(
                    "AutomationProperties.LiveSetting")?.Value),
            $"{Path.GetFileName(file)} の動的メッセージに"
            + "LiveSettingがありません。");
      }
    }
  }

  [Fact]
  public void BackupOptionsMatchSpecificationDefaults()
  {
    XNamespace xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    var projectRoot = FindProjectRoot();
    var document = XDocument.Load(Path.Combine(
        projectRoot,
        "src",
        "Ytec.BrowserCapsule.App",
        "BackupOptionsDialog.xaml"));
    var checkBoxes = document.Descendants(Presentation + "CheckBox")
        .Where(element => element.Attribute(xaml + "Name") is not null)
        .ToDictionary(
            element => element.Attribute(xaml + "Name")!.Value,
            element => string.Equals(
                element.Attribute("IsChecked")?.Value,
                "True",
                StringComparison.OrdinalIgnoreCase),
            StringComparer.Ordinal);

    foreach (var enabledByDefault in new[]
             {
               "SettingsCheckBox",
               "BookmarksCheckBox",
               "HistoryCheckBox",
               "ExtensionsCheckBox",
               "SiteDataCheckBox",
               "PasswordCsvCheckBox",
             })
    {
      Assert.True(
          checkBoxes[enabledByDefault],
          $"{enabledByDefault} は仕様どおり初期選択にしてください。");
    }

    Assert.False(checkBoxes["SessionsCheckBox"]);
    Assert.False(checkBoxes["FullProfileCheckBox"]);
  }

  [Fact]
  public void ManualButtonOpensBundledScreenshotManual()
  {
    XNamespace xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    var projectRoot = FindProjectRoot();
    var appDirectory = Path.Combine(
        projectRoot,
        "src",
        "Ytec.BrowserCapsule.App");
    var window = XDocument.Load(
        Path.Combine(appDirectory, "MainWindow.xaml"));
    var manualButton = window
        .Descendants(Presentation + "Button")
        .Single(element =>
            element.Attribute(xaml + "Name")?.Value == "ManualButton");

    Assert.Equal(
        "操作マニュアル",
        manualButton.Attribute("Content")?.Value);
    Assert.Equal(
        "操作マニュアル",
        manualButton.Attribute("AutomationProperties.Name")?.Value);
    Assert.Equal(
        "ManualButton_Click",
        manualButton.Attribute("Click")?.Value);

    var project = XDocument.Load(Path.Combine(
        appDirectory,
        "Ytec.BrowserCapsule.App.csproj"));
    var manualContents = project
        .Descendants("Content")
        .Where(element =>
            element.Attribute("Include")?.Value.EndsWith(
                ".pdf",
                StringComparison.OrdinalIgnoreCase) == true)
        .ToArray();

    Assert.Equal(2, manualContents.Length);
    var manualContent = manualContents.Single(element =>
        element.Attribute("Include")?.Value.EndsWith(
            "Y-TEC_Browser_Capsule_操作マニュアル_1.2.1.pdf",
            StringComparison.Ordinal) == true);
    Assert.Equal(
        "操作マニュアル.pdf",
        manualContent.Element("Link")?.Value);
    Assert.Equal(
        "PreserveNewest",
        manualContent.Element("CopyToPublishDirectory")?.Value);
    var englishManualContent = manualContents.Single(element =>
        element.Attribute("Include")?.Value.EndsWith(
            "Y-TEC_Browser_Capsule_User_Manual_1.2.1.pdf",
            StringComparison.Ordinal) == true);
    Assert.Equal(
        "Operation Manual.pdf",
        englishManualContent.Element("Link")?.Value);
  }

  [Fact]
  public void PasswordCsvAssistantCanSkipWhileWaiting()
  {
    var projectRoot = FindProjectRoot();
    var source = File.ReadAllText(Path.Combine(
        projectRoot,
        "src",
        "Ytec.BrowserCapsule.App",
        "PasswordCsvAssistantDialog.xaml.cs"));

    Assert.Contains(
        "private async void SkipButton_Click(",
        source,
        StringComparison.Ordinal);
    Assert.Contains(
        "await CancelCaptureAsync();",
        source,
        StringComparison.Ordinal);
    Assert.Contains(
        "await item.Session.DisposeAsync();",
        source,
        StringComparison.Ordinal);
    Assert.Contains(
        "SkipButton.IsEnabled = Current.Capture is null;",
        source,
        StringComparison.Ordinal);
    Assert.DoesNotContain(
        "SkipButton.IsEnabled = !isBusy && Current.Capture is null;",
        source,
        StringComparison.Ordinal);
  }

  private static string[] GetAppXamlFiles()
  {
    var projectRoot = FindProjectRoot();
    return Directory.GetFiles(
        Path.Combine(
            projectRoot,
            "src",
            "Ytec.BrowserCapsule.App"),
        "*.xaml",
        SearchOption.TopDirectoryOnly);
  }

  private static string FindProjectRoot()
  {
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
      if (File.Exists(
          Path.Combine(current.FullName, "YtecBrowserCapsule.sln")))
      {
        return current.FullName;
      }

      current = current.Parent;
    }

    throw new DirectoryNotFoundException(
        "YtecBrowserCapsule.slnを見つけられません。");
  }
}
