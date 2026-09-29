namespace Ytec.BrowserCapsule.Domain.BrowserDiscovery;

/// <summary>
/// ブラウザープロセスを実行ファイル単位で照合する規則です。
/// </summary>
public sealed record ProcessMatchRule(
    string ProcessName,
    IReadOnlyList<string> ExpectedExecutablePaths);
