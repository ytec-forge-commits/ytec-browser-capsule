using Ytec.BrowserCapsule.App.Localization;
using Ytec.BrowserCapsule.Domain.Settings;

namespace Ytec.BrowserCapsule.App.Tests.Localization;

public sealed class UiTextTests
{
  [Fact]
  public void JapaneseUsesSourceTextAndJapaneseManual()
  {
    UiText.Initialize(AppLanguage.Japanese);

    Assert.False(UiText.IsEnglish);
    Assert.Equal("設定", UiText.T("設定"));
    Assert.Equal("操作マニュアル.pdf", UiText.ManualFileName);
  }

  [Fact]
  public void EnglishTranslatesStaticAndDynamicText()
  {
    UiText.Initialize(AppLanguage.English);

    Assert.True(UiText.IsEnglish);
    Assert.Equal("Settings", UiText.T("設定"));
    Assert.Equal(
        "Found 3 browsers and 6 profiles",
        UiText.T("3ブラウザー、6プロファイルを検出しました"));
    Assert.Equal(
        "CSV records: 12 (values are not displayed)",
        UiText.T("CSVのレコード数: 12件（値は表示しません）"));
    Assert.Equal("Operation Manual.pdf", UiText.ManualFileName);
  }
}
