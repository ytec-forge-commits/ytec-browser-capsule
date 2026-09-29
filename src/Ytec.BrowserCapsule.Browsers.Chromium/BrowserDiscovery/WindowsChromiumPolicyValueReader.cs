using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Ytec.BrowserCapsule.Browsers.Chromium.BrowserDiscovery;

/// <summary>
/// 現在のWindows環境からブラウザーポリシーを読み取ります。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsChromiumPolicyValueReader : IChromiumPolicyValueReader
{
  public string? ReadCurrentUser(string policySubKey)
  {
    return ReadValue(RegistryHive.CurrentUser, policySubKey);
  }

  public string? ReadLocalMachine(string policySubKey)
  {
    return ReadValue(RegistryHive.LocalMachine, policySubKey);
  }

  private static string? ReadValue(
      RegistryHive hive,
      string policySubKey)
  {
    foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
    {
      try
      {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(policySubKey, writable: false);
        if (key?.GetValue(
            "UserDataDir",
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames) is string value
            && !string.IsNullOrWhiteSpace(value))
        {
          return value;
        }
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or System.Security.SecurityException)
      {
        // 読めないレジストリービューは候補に加えず、次の検出手段へ進みます。
      }
    }

    return null;
  }
}
