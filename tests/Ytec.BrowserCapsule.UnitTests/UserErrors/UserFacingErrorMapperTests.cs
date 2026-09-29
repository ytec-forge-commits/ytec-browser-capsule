using Ytec.BrowserCapsule.Application.UserErrors;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Domain.ProfileBackups;
using Ytec.BrowserCapsule.Domain.ProfileRestores;

namespace Ytec.BrowserCapsule.UnitTests.UserErrors;

public sealed class UserFacingErrorMapperTests
{
  public static TheoryData<Exception, UserOperation, string> KnownErrors =>
      new()
      {
        {
          new BrowserMustBeClosedException("Google Chrome"),
          UserOperation.Backup,
          "YBC-PROC-001"
        },
        {
          new BackupContainerIntegrityException(),
          UserOperation.Verification,
          "YBC-CRYPTO-001"
        },
        {
          new InvalidBackupEntryPathException("synthetic"),
          UserOperation.Restore,
          "YBC-REST-001"
        },
        {
          new RestorePlanException("synthetic"),
          UserOperation.Restore,
          "YBC-REST-002"
        },
        {
          new PasswordCsvValidationException("synthetic"),
          UserOperation.PasswordCsv,
          "YBC-CSV-002"
        },
        {
          new BackupPlanException("synthetic"),
          UserOperation.Backup,
          "YBC-BACK-001"
        },
        {
          new UnauthorizedAccessException("synthetic"),
          UserOperation.Backup,
          "YBC-IO-002"
        },
      };

  [Theory]
  [MemberData(nameof(KnownErrors))]
  public void KnownExceptionHasStableCode(
      Exception exception,
      UserOperation operation,
      string expectedCode)
  {
    var result = UserFacingErrorMapper.Map(exception, operation);

    Assert.Equal(expectedCode, result.Code);
    Assert.NotEmpty(result.Message);
    Assert.NotEmpty(result.SuggestedAction);
  }

  [Fact]
  public void IoMessageWithPrivatePathIsNotExposed()
  {
    const string privatePath =
        @"C:\Users\Synthetic User\secret\profile.db";
    var result = UserFacingErrorMapper.Map(
        new IOException($"Access failed: {privatePath}"),
        UserOperation.Restore);

    Assert.Equal("YBC-IO-003", result.Code);
    Assert.DoesNotContain(
        privatePath,
        result.Message,
        StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(
        privatePath,
        result.SuggestedAction,
        StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void UnexpectedExceptionDoesNotExposeOriginalMessage()
  {
    const string secret = "synthetic-secret-value";
    var result = UserFacingErrorMapper.Map(
        new InvalidOperationException(secret),
        UserOperation.PasswordCsv);

    Assert.Equal("YBC-UNEXPECTED-001", result.Code);
    Assert.DoesNotContain(secret, result.Message, StringComparison.Ordinal);
    Assert.DoesNotContain(
        secret,
        result.SuggestedAction,
        StringComparison.Ordinal);
  }
}
