using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Ytec.BrowserCapsule.Infrastructure.PasswordCsv;
using Ytec.BrowserCapsule.SecurityTests.BackupContainers;

namespace Ytec.BrowserCapsule.SecurityTests.PasswordCsv;

[SupportedOSPlatform("windows")]
public sealed class SecurePasswordCsvStagingFactoryTests
{
  [Fact]
  public async Task SessionAclAllowsOnlyCurrentUserAndSystem()
  {
    using var temporary = new TemporaryDirectory();
    var root = temporary.GetPath("staging");
    var factory = new SecurePasswordCsvStagingFactory(root);
    await using var session = await factory.CreateAsync(
        CancellationToken.None);

    var security = FileSystemAclExtensions.GetAccessControl(
        new DirectoryInfo(session.StagingDirectory));
    Assert.True(security.AreAccessRulesProtected);
    var rules = security
        .GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier))
        .Cast<FileSystemAccessRule>()
        .ToArray();
    using var identity = WindowsIdentity.GetCurrent();
    var currentSid = identity.User?.Value;
    var systemSid = new SecurityIdentifier(
        WellKnownSidType.LocalSystemSid,
        domainSid: null).Value;

    Assert.NotEmpty(rules);
    Assert.All(
        rules,
        rule =>
        {
          Assert.False(rule.IsInherited);
          Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
          var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
          Assert.True(
              string.Equals(
                  sid,
                  currentSid,
                  StringComparison.Ordinal)
              || string.Equals(
                  sid,
                  systemSid,
                  StringComparison.Ordinal));
        });
  }

  [Fact]
  public async Task StableCsvIsEncryptedAndPlaintextIsDeleted()
  {
    using var temporary = new TemporaryDirectory();
    var factory = new SecurePasswordCsvStagingFactory(
        temporary.GetPath("staging"));
    await using var session = await factory.CreateAsync(
        CancellationToken.None);
    var csvPath = Path.Combine(
        session.StagingDirectory,
        "passwords.csv");
    const string csv =
        "url,username,password\r\n"
        + "https://example.invalid,user,synthetic-test-only\r\n";
    await File.WriteAllTextAsync(csvPath, csv);

    var stablePath = await session.WaitForStableCsvAsync(
        TimeSpan.FromSeconds(3),
        CancellationToken.None);
    Assert.Equal(csvPath, stablePath);
    await using var capture = await session.ProtectAsync(
        stablePath!,
        allowEmpty: false,
        CancellationToken.None);

    Assert.False(File.Exists(csvPath));
    Assert.Null(capture.ResidualPlaintextPath);
    Assert.Equal(1, capture.Validation.RecordCount);
    await using var decrypted = await capture.OpenReadAsync(
        CancellationToken.None);
    using var reader = new StreamReader(decrypted);
    Assert.Equal(csv, await reader.ReadToEndAsync());
  }

  [Fact]
  public async Task WaitingCanBeCanceledAndSessionCleanupRemovesStaging()
  {
    using var temporary = new TemporaryDirectory();
    var factory = new SecurePasswordCsvStagingFactory(
        temporary.GetPath("staging"));
    var session = await factory.CreateAsync(CancellationToken.None);
    var stagingDirectory = session.StagingDirectory;
    using var cancellation = new CancellationTokenSource();

    var waitTask = session.WaitForStableCsvAsync(
        TimeSpan.FromMinutes(1),
        cancellation.Token);
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => waitTask);
    await session.DisposeAsync();
    Assert.False(Directory.Exists(stagingDirectory));
  }

  [Fact]
  public async Task StaleGuidSessionIsRemovedWithoutTouchingOtherFolder()
  {
    using var temporary = new TemporaryDirectory();
    var root = temporary.GetPath("staging");
    var stale = Path.Combine(root, Guid.NewGuid().ToString("N"));
    var unrelated = Path.Combine(root, "keep");
    Directory.CreateDirectory(stale);
    Directory.CreateDirectory(unrelated);
    await File.WriteAllTextAsync(
        Path.Combine(stale, "leftover.csv"),
        "do not parse");
    await File.WriteAllTextAsync(
        Path.Combine(unrelated, "keep.txt"),
        "keep");
    var factory = new SecurePasswordCsvStagingFactory(root);

    await factory.CleanupStaleSessionsAsync(CancellationToken.None);

    Assert.False(Directory.Exists(stale));
    Assert.True(File.Exists(Path.Combine(unrelated, "keep.txt")));
  }
}
