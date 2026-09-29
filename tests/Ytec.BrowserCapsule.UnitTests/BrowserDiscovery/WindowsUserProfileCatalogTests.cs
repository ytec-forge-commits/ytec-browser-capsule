using System.Runtime.Versioning;
using Ytec.BrowserCapsule.Infrastructure.WindowsUserProfiles;

namespace Ytec.BrowserCapsule.UnitTests.BrowserDiscovery;

[SupportedOSPlatform("windows")]
public sealed class WindowsUserProfileCatalogTests
{
  [Fact]
  public void OtherUsersAreOptInAndUnreadableProfilesAreSkipped()
  {
    using var temporary = new TemporaryDirectory();
    var current = temporary.CreateDirectory("current");
    var readable = temporary.CreateDirectory("readable");
    var unreadable = temporary.CreateDirectory("unreadable");
    var candidates = new[]
    {
      new WindowsUserProfileCandidate("S-1-test-current", current),
      new WindowsUserProfileCandidate("S-1-test-readable", readable),
      new WindowsUserProfileCandidate("S-1-test-unreadable", unreadable),
    };
    var catalog = new WindowsUserProfileCatalog(
        current,
        "current-user",
        () => candidates,
        path => !string.Equals(
            path,
            unreadable,
            StringComparison.OrdinalIgnoreCase));

    var currentOnly = catalog.Discover(includeOtherUsers: false);
    var withOthers = catalog.Discover(includeOtherUsers: true);

    Assert.Single(currentOnly.Profiles);
    Assert.True(currentOnly.Profiles[0].IsCurrentUser);
    Assert.Equal(2, withOthers.Profiles.Count);
    Assert.Contains(
        withOthers.Profiles,
        profile => profile.DisplayName == "readable"
            && !profile.IsCurrentUser);
    Assert.Equal(1, withOthers.SkippedProfileCount);
  }

  [Fact]
  public void StableIdDoesNotContainSidOrProfilePath()
  {
    var id = WindowsUserProfileCatalog.CreateStableId(
        "S-1-5-21-synthetic",
        @"C:\Users\Synthetic");

    Assert.Equal(24, id.Length);
    Assert.DoesNotContain("S-1", id, StringComparison.Ordinal);
    Assert.DoesNotContain("Synthetic", id, StringComparison.Ordinal);
  }
}
