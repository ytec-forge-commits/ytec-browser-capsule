using System.Text;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Infrastructure.PasswordCsv;
using Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

namespace Ytec.BrowserCapsule.UnitTests.PasswordCsv;

public sealed class PasswordCsvValidatorTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AcceptsOfficialColumnsQuotedNewlineAndExtraColumns(
      bool includeBom)
  {
    using var temporary = new TemporaryDirectory();
    var path = Path.Combine(temporary.Path, "passwords.csv");
    var csv =
        "name,url,username,password,note\r\n"
        + "\"表示名\",\"https://example.invalid/a,b\",\"user\","
        + "\"not-a-real-password\",\"line1\r\nline2\"\r\n";
    var encoding = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: includeBom);
    await File.WriteAllTextAsync(path, csv, encoding);

    var result = await PasswordCsvValidator.ValidateAsync(
        path,
        allowEmpty: false,
        CancellationToken.None);

    Assert.Equal(1, result.RecordCount);
    Assert.Contains(
        result.HeaderNames,
        header => string.Equals(
            header,
            "password",
            StringComparison.OrdinalIgnoreCase));
    Assert.Equal(new FileInfo(path).Length, result.FileBytes);
  }

  [Fact]
  public async Task AcceptsFirefoxAdditionalColumns()
  {
    using var temporary = new TemporaryDirectory();
    var path = temporary.WriteFile(
        "firefox.csv",
        "url,username,password,httpRealm,guid\n"
        + "https://example.invalid,user,test,{realm},id\n");

    var result = await PasswordCsvValidator.ValidateAsync(
        path,
        allowEmpty: false,
        CancellationToken.None);

    Assert.Equal(1, result.RecordCount);
  }

  [Fact]
  public async Task RejectsMissingRequiredColumn()
  {
    using var temporary = new TemporaryDirectory();
    var path = temporary.WriteFile(
        "missing.csv",
        "url,username\nhttps://example.invalid,user\n");

    await Assert.ThrowsAsync<PasswordCsvValidationException>(
        () => PasswordCsvValidator.ValidateAsync(
            path,
            allowEmpty: false,
            CancellationToken.None));
  }

  [Fact]
  public async Task EmptyCsvRequiresExplicitPermission()
  {
    using var temporary = new TemporaryDirectory();
    var path = temporary.WriteFile("empty.csv", string.Empty);

    await Assert.ThrowsAsync<PasswordCsvValidationException>(
        () => PasswordCsvValidator.ValidateAsync(
            path,
            allowEmpty: false,
            CancellationToken.None));
    var accepted = await PasswordCsvValidator.ValidateAsync(
        path,
        allowEmpty: true,
        CancellationToken.None);

    Assert.Empty(accepted.HeaderNames);
    Assert.Equal(0, accepted.RecordCount);
  }

  [Fact]
  public async Task RejectsInvalidUtf8AndUnclosedQuotes()
  {
    using var temporary = new TemporaryDirectory();
    var invalidUtf8 = Path.Combine(temporary.Path, "invalid.csv");
    await File.WriteAllBytesAsync(
        invalidUtf8,
        [0x75, 0x72, 0x6C, 0x2C, 0xFF]);
    var invalidQuotes = temporary.WriteFile(
        "quotes.csv",
        "url,username,password\n\"https://example.invalid,user,test");

    await Assert.ThrowsAsync<PasswordCsvValidationException>(
        () => PasswordCsvValidator.ValidateAsync(
            invalidUtf8,
            allowEmpty: false,
            CancellationToken.None));
    await Assert.ThrowsAsync<PasswordCsvValidationException>(
        () => PasswordCsvValidator.ValidateAsync(
            invalidQuotes,
            allowEmpty: false,
            CancellationToken.None));
  }
}
