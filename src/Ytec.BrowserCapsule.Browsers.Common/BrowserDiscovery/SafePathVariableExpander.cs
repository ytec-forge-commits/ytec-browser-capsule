using System.Text.RegularExpressions;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;

namespace Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;

/// <summary>
/// ブラウザーポリシーで使われる既知の変数だけを絶対パスへ展開します。
/// </summary>
public sealed partial class SafePathVariableExpander
{
  private readonly Dictionary<string, string> _variables;

  public SafePathVariableExpander(IReadOnlyDictionary<string, string> variables)
  {
    ArgumentNullException.ThrowIfNull(variables);
    _variables = new Dictionary<string, string>(
        variables,
        StringComparer.OrdinalIgnoreCase);
  }

  public static SafePathVariableExpander CreateForCurrentUser()
  {
    var userProfile = Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile);
    return Create(
        userProfile,
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments),
        Environment.UserName);
  }

  public static SafePathVariableExpander CreateForWindowsUser(
      WindowsUserProfileIdentity profile)
  {
    ArgumentNullException.ThrowIfNull(profile);
    var userProfile = Path.GetFullPath(profile.ProfilePath);
    var localApplicationData = profile.IsCurrentUser
        ? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData)
        : Path.Combine(userProfile, "AppData", "Local");
    var roamingApplicationData = profile.IsCurrentUser
        ? Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData)
        : Path.Combine(userProfile, "AppData", "Roaming");
    var documents = profile.IsCurrentUser
        ? Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments)
        : Path.Combine(userProfile, "Documents");
    return Create(
        userProfile,
        localApplicationData,
        roamingApplicationData,
        documents,
        profile.DisplayName);
  }

  private static SafePathVariableExpander Create(
      string userProfile,
      string localApplicationData,
      string roamingApplicationData,
      string documents,
      string userName)
  {
    var variables = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase)
    {
      ["local_app_data"] = localApplicationData,
      ["localappdata"] = localApplicationData,
      ["roaming_app_data"] = roamingApplicationData,
      ["appdata"] = roamingApplicationData,
      ["profile"] = userProfile,
      ["userprofile"] = userProfile,
      ["documents"] = documents,
      ["global_app_data"] = Environment.GetFolderPath(
          Environment.SpecialFolder.CommonApplicationData),
      ["program_files"] = Environment.GetFolderPath(
          Environment.SpecialFolder.ProgramFiles),
      ["program_files_x86"] = Environment.GetFolderPath(
          Environment.SpecialFolder.ProgramFilesX86),
      ["windows"] = Environment.GetFolderPath(
          Environment.SpecialFolder.Windows),
      ["user_name"] = userName,
      ["username"] = userName,
      ["machine_name"] = Environment.MachineName,
      ["computername"] = Environment.MachineName,
    };

    return new SafePathVariableExpander(variables);
  }

  public string? TryExpandAbsolutePath(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    var unresolved = false;
    var expanded = BracedVariableRegex().Replace(
        value.Trim().Trim('"'),
        match => Resolve(match.Groups[1].Value, match.Value, ref unresolved));
    expanded = PercentVariableRegex().Replace(
        expanded,
        match => Resolve(match.Groups[1].Value, match.Value, ref unresolved));

    if (unresolved || BracedVariableRegex().IsMatch(expanded)
        || PercentVariableRegex().IsMatch(expanded))
    {
      return null;
    }

    try
    {
      return Path.IsPathFullyQualified(expanded)
          ? Path.GetFullPath(expanded)
          : null;
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or NotSupportedException
        or PathTooLongException)
    {
      return null;
    }
  }

  private string Resolve(
      string name,
      string original,
      ref bool unresolved)
  {
    if (_variables.TryGetValue(name, out var replacement)
        && !string.IsNullOrWhiteSpace(replacement))
    {
      return replacement;
    }

    unresolved = true;
    return original;
  }

  [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.CultureInvariant)]
  private static partial Regex BracedVariableRegex();

  [GeneratedRegex(@"%([^%]+)%", RegexOptions.CultureInvariant)]
  private static partial Regex PercentVariableRegex();
}
