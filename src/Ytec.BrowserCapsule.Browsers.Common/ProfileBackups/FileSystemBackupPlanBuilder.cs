using Ytec.BrowserCapsule.Browsers.Common.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.BrowserDiscovery;
using Ytec.BrowserCapsule.Domain.ProfileBackups;

namespace Ytec.BrowserCapsule.Browsers.Common.ProfileBackups;

/// <summary>
/// 許可リストと除外設定から、リンクを追跡しない固定ファイル一覧を作成します。
/// </summary>
public sealed class FileSystemBackupPlanBuilder
{
  private const int MaximumReadAttempts = 3;
  private readonly BrowserBackupRuleSet _rules;
  private readonly TimeSpan _initialRetryDelay;

  public FileSystemBackupPlanBuilder(
      BrowserBackupRuleSet rules,
      TimeSpan? initialRetryDelay = null)
  {
    ArgumentNullException.ThrowIfNull(rules);
    _rules = rules;
    _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromMilliseconds(50);
  }

  public async Task<BrowserBackupPlan> BuildAsync(
      BrowserProfile profile,
      string backupProfileId,
      BackupComponent selection,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(profile);
    ArgumentException.ThrowIfNullOrWhiteSpace(backupProfileId);
    if (selection is BackupComponent.None)
    {
      throw new ArgumentException(
          "バックアップ対象が選択されていません。",
          nameof(selection));
    }

    if (!Directory.Exists(profile.AbsoluteProfilePath)
        || FileSystemSafety.IsReparsePoint(profile.AbsoluteProfilePath))
    {
      throw new BackupPlanException(
          $"「{profile.DisplayName}」のプロファイルを安全に読み取れません。");
    }

    var files = new Dictionary<string, BackupPlanFile>(
        StringComparer.OrdinalIgnoreCase);
    var warnings = new List<BackupPlanWarning>();

    if ((selection & BackupComponent.FullProfile) != 0)
    {
      await AddDirectoryAsync(
          profile,
          backupProfileId,
          profile.AbsoluteProfilePath,
          BackupComponent.FullProfile,
          isRequired: false,
          files,
          warnings,
          cancellationToken).ConfigureAwait(false);
    }
    else
    {
      foreach (var rule in _rules.ComponentPaths)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if ((selection & rule.Component) == 0)
        {
          continue;
        }

        var candidate = ResolveWithinProfile(
            profile.AbsoluteProfilePath,
            rule.RelativePath);
        if (candidate is null)
        {
          throw new BackupPlanException(
              "バックアップ許可リストに安全でないパスがあります。");
        }

        if (File.Exists(candidate))
        {
          await AddFileAsync(
              profile,
              backupProfileId,
              candidate,
              rule.Component,
              rule.IsRequired,
              files,
              warnings,
              cancellationToken).ConfigureAwait(false);
        }
        else if (Directory.Exists(candidate))
        {
          await AddDirectoryAsync(
              profile,
              backupProfileId,
              candidate,
              rule.Component,
              rule.IsRequired,
              files,
              warnings,
              cancellationToken).ConfigureAwait(false);
        }
        else if (rule.IsRequired)
        {
          throw new BackupPlanException(
              $"「{profile.DisplayName}」の重要な設定ファイルがありません。");
        }
      }
    }

    var orderedFiles = files.Values
        .OrderBy(file => file.EntryPath, StringComparer.Ordinal)
        .ToArray();
    long totalBytes = 0;
    foreach (var file in orderedFiles)
    {
      totalBytes = checked(totalBytes + file.Length);
    }

    return new BrowserBackupPlan(
        profile,
        backupProfileId,
        selection,
        orderedFiles,
        warnings,
        totalBytes);
  }

  private async Task AddDirectoryAsync(
      BrowserProfile profile,
      string backupProfileId,
      string startingDirectory,
      BackupComponent component,
      bool isRequired,
      Dictionary<string, BackupPlanFile> files,
      List<BackupPlanWarning> warnings,
      CancellationToken cancellationToken)
  {
    var pending = new Stack<string>();
    pending.Push(startingDirectory);

    while (pending.Count > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var directory = pending.Pop();
      if (IsExcludedDirectory(directory)
          || FileSystemSafety.IsReparsePoint(directory))
      {
        if (FileSystemSafety.IsReparsePoint(directory))
        {
          warnings.Add(new BackupPlanWarning(
              "reparse-point-skipped",
              "リンクまたはジャンクションを1件読み飛ばしました。"));
        }

        continue;
      }

      string[] childDirectories;
      string[] childFiles;
      try
      {
        childDirectories = Directory.GetDirectories(
            directory,
            "*",
            SearchOption.TopDirectoryOnly);
        childFiles = Directory.GetFiles(
            directory,
            "*",
            SearchOption.TopDirectoryOnly);
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or DirectoryNotFoundException)
      {
        if (isRequired)
        {
          throw new BackupPlanException(
              $"「{profile.DisplayName}」の重要な設定を列挙できません。",
              exception);
        }

        warnings.Add(new BackupPlanWarning(
            "directory-unreadable",
            "読み取れない任意フォルダーを1件除外しました。"));
        continue;
      }

      foreach (var childDirectory in childDirectories)
      {
        pending.Push(childDirectory);
      }

      foreach (var childFile in childFiles)
      {
        await AddFileAsync(
            profile,
            backupProfileId,
            childFile,
            component,
            isRequired: false,
            files,
            warnings,
            cancellationToken).ConfigureAwait(false);
      }
    }
  }

  private async Task AddFileAsync(
      BrowserProfile profile,
      string backupProfileId,
      string filePath,
      BackupComponent component,
      bool isRequired,
      Dictionary<string, BackupPlanFile> files,
      List<BackupPlanWarning> warnings,
      CancellationToken cancellationToken)
  {
    if (IsExcludedFile(filePath)
        || FileSystemSafety.IsReparsePoint(filePath))
    {
      if (FileSystemSafety.IsReparsePoint(filePath))
      {
        warnings.Add(new BackupPlanWarning(
            "reparse-point-skipped",
            "リンクを1件読み飛ばしました。"));
      }

      return;
    }

    var relativePath = Path.GetRelativePath(
        profile.AbsoluteProfilePath,
        filePath);
    if (relativePath.StartsWith("..", StringComparison.Ordinal)
        || Path.IsPathFullyQualified(relativePath))
    {
      throw new BackupPlanException(
          "プロファイル外のファイルをバックアップしようとしました。");
    }

    var fileInfo = await TryReadFileInfoAsync(
        filePath,
        cancellationToken).ConfigureAwait(false);
    if (fileInfo is null)
    {
      if (isRequired)
      {
        throw new BackupPlanException(
            $"「{profile.DisplayName}」の重要なファイルを読み取れません。");
      }

      warnings.Add(new BackupPlanWarning(
          "optional-file-unreadable",
          $"任意ファイル「{Path.GetFileName(filePath)}」を除外しました。"));
      return;
    }

    var entryPath = string.Join(
        '/',
        "profiles",
        profile.BrowserId,
        backupProfileId,
        "profile",
        relativePath.Replace(
            Path.DirectorySeparatorChar,
            '/'));
    entryPath = BackupEntryPathValidator.Validate(entryPath);

    files.TryAdd(
        filePath,
        new BackupPlanFile(
            Path.GetFullPath(filePath),
            entryPath,
            fileInfo.Value.Length,
            fileInfo.Value.LastWriteUtc,
            component,
            isRequired));
  }

  private async Task<(long Length, DateTimeOffset LastWriteUtc)?>
      TryReadFileInfoAsync(
          string filePath,
          CancellationToken cancellationToken)
  {
    for (var attempt = 0; attempt < MaximumReadAttempts; attempt++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var fileInfo = new FileInfo(filePath);
        fileInfo.Refresh();
        return (
            fileInfo.Length,
            new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero));
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException
          or FileNotFoundException)
      {
        if (attempt == MaximumReadAttempts - 1)
        {
          return null;
        }

        var multiplier = 1 << attempt;
        await Task.Delay(
            TimeSpan.FromTicks(
                _initialRetryDelay.Ticks * multiplier),
            cancellationToken).ConfigureAwait(false);
      }
    }

    return null;
  }

  private bool IsExcludedDirectory(string directoryPath)
  {
    return _rules.ExcludedDirectoryNames.Contains(
        Path.GetFileName(directoryPath));
  }

  private bool IsExcludedFile(string filePath)
  {
    var fileName = Path.GetFileName(filePath);
    return _rules.ExcludedFileNames.Contains(fileName)
        || _rules.ExcludedFileSuffixes.Any(
            suffix => fileName.EndsWith(
                suffix,
                StringComparison.OrdinalIgnoreCase));
  }

  private static string? ResolveWithinProfile(
      string profileRoot,
      string relativePath)
  {
    try
    {
      var fullPath = Path.GetFullPath(
          Path.Combine(profileRoot, relativePath));
      return FileSystemSafety.IsWithinRoot(profileRoot, fullPath)
          ? fullPath
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
}
