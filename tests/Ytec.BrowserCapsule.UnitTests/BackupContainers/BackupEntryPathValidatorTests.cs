using Ytec.BrowserCapsule.Domain.BackupContainers;

namespace Ytec.BrowserCapsule.UnitTests.BackupContainers;

public sealed class BackupEntryPathValidatorTests
{
  [Theory]
  [InlineData("../outside")]
  [InlineData("profiles/../outside")]
  [InlineData(@"C:\absolute")]
  [InlineData(@"\\server\share")]
  [InlineData("/rooted")]
  [InlineData("profiles//file")]
  [InlineData("profiles/file.")]
  [InlineData("profiles/file ")]
  [InlineData("profiles/file:stream")]
  [InlineData("profiles/CON")]
  [InlineData("profiles/aux.txt")]
  [InlineData("profiles/LPT9.data")]
  [InlineData(@"profiles\windows-separator")]
  public void UnsafeWindowsPathsAreRejected(string path)
  {
    Assert.Throws<InvalidBackupEntryPathException>(
        () => BackupEntryPathValidator.Validate(path));
  }

  [Fact]
  public void CaseInsensitiveCollisionsAreRejected()
  {
    Assert.Throws<InvalidBackupEntryPathException>(
        () => BackupEntryPathValidator.ValidateNoCaseInsensitiveCollisions(
        [
          "profiles/chrome/p1/Bookmarks",
          "profiles/chrome/p1/bookmarks",
        ]));
  }

  [Fact]
  public void CanonicalUnicodeRelativePathIsAccepted()
  {
    const string path = "profiles/firefox/p1/設定/ブックマーク.json";
    Assert.Equal(path, BackupEntryPathValidator.Validate(path));
  }

  [Fact]
  public void ExcessiveUtf8LengthIsRejected()
  {
    var path = string.Join(
        '/',
        Enumerable.Repeat(new string('あ', 200), 10));

    Assert.Throws<InvalidBackupEntryPathException>(
        () => BackupEntryPathValidator.Validate(path));
  }
}
