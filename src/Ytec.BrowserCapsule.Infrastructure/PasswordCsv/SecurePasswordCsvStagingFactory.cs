using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Domain.PasswordCsv;
using Ytec.BrowserCapsule.Infrastructure.BackupContainers;

namespace Ytec.BrowserCapsule.Infrastructure.PasswordCsv;

/// <summary>
/// 現在ユーザーとSYSTEMだけへ権限を限定したCSV作業領域を管理します。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SecurePasswordCsvStagingFactory :
    IPasswordCsvStagingFactory
{
  private readonly string _stagingRoot;

  public SecurePasswordCsvStagingFactory()
      : this(
          Path.Combine(
              Environment.GetFolderPath(
                  Environment.SpecialFolder.LocalApplicationData),
              "Y-TEC",
              "BrowserCapsule",
              "Staging"))
  {
  }

  public SecurePasswordCsvStagingFactory(string stagingRoot)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
    _stagingRoot = ValidateLocalAbsolutePath(stagingRoot);
  }

  public Task CleanupStaleSessionsAsync(
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (!Directory.Exists(_stagingRoot))
    {
      return Task.CompletedTask;
    }

    foreach (var directory in Directory.EnumerateDirectories(
        _stagingRoot,
        "*",
        SearchOption.TopDirectoryOnly))
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!Guid.TryParseExact(
          Path.GetFileName(directory),
          "N",
          out _)
          || IsReparsePoint(directory))
      {
        continue;
      }

      TryDeleteTree(directory);
    }

    return Task.CompletedTask;
  }

  public Task<IPasswordCsvStagingSession> CreateAsync(
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    Directory.CreateDirectory(_stagingRoot);
    if (IsReparsePoint(_stagingRoot))
    {
      throw new IOException(
          "CSV一時領域がリンクまたはジャンクションです。");
    }

    var sessionPath = Path.Combine(
        _stagingRoot,
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(sessionPath);
    try
    {
      ApplyRestrictedAcl(sessionPath);
      return Task.FromResult<IPasswordCsvStagingSession>(
          new SecurePasswordCsvStagingSession(
              sessionPath));
    }
    catch
    {
      TryDeleteTree(sessionPath);
      throw;
    }
  }

  private static void ApplyRestrictedAcl(string directoryPath)
  {
    using var identity = WindowsIdentity.GetCurrent();
    var currentSid = identity.User
        ?? throw new InvalidOperationException(
            "現在のWindowsユーザーSIDを取得できません。");
    var systemSid = new SecurityIdentifier(
        WellKnownSidType.LocalSystemSid,
        domainSid: null);
    var inheritance =
        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
    var security = new DirectorySecurity();
    security.SetAccessRuleProtection(
        isProtected: true,
        preserveInheritance: false);
    security.AddAccessRule(new FileSystemAccessRule(
        currentSid,
        FileSystemRights.FullControl,
        inheritance,
        PropagationFlags.None,
        AccessControlType.Allow));
    security.AddAccessRule(new FileSystemAccessRule(
        systemSid,
        FileSystemRights.FullControl,
        inheritance,
        PropagationFlags.None,
        AccessControlType.Allow));
    FileSystemAclExtensions.SetAccessControl(
        new DirectoryInfo(directoryPath),
        security);
  }

  private static string ValidateLocalAbsolutePath(string path)
  {
    if (!Path.IsPathFullyQualified(path)
        || path.StartsWith(
            new string(Path.DirectorySeparatorChar, 2),
            StringComparison.Ordinal))
    {
      throw new ArgumentException(
          "CSV一時領域にはローカルの絶対パスが必要です。",
          nameof(path));
    }

    return Path.GetFullPath(path);
  }

  private static bool IsReparsePoint(string path)
  {
    return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
  }

  private static void TryDeleteTree(string directoryPath)
  {
    try
    {
      if (Directory.Exists(directoryPath)
          && !IsReparsePoint(directoryPath))
      {
        Directory.Delete(directoryPath, recursive: true);
      }
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException)
    {
      // 次回起動時に再試行します。CSV本文は読み取りません。
    }
  }

  private sealed class SecurePasswordCsvStagingSession :
      IPasswordCsvStagingSession
  {
    private readonly List<IProtectedPasswordCsv> _captures = [];
    private bool _disposed;

    public SecurePasswordCsvStagingSession(
        string stagingDirectory)
    {
      StagingDirectory = stagingDirectory;
    }

    public string StagingDirectory { get; }

    public async Task<string?> WaitForStableCsvAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
      ThrowIfDisposed();
      ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
          timeout,
          TimeSpan.Zero);

      using var watcher = new FileSystemWatcher(
          StagingDirectory,
          "*.csv")
      {
        IncludeSubdirectories = false,
        NotifyFilter =
            NotifyFilters.FileName
            | NotifyFilters.Size
            | NotifyFilters.LastWrite,
        EnableRaisingEvents = true,
      };
      var signal = new TaskCompletionSource(
          TaskCreationOptions.RunContinuationsAsynchronously);
      FileSystemEventHandler changed = (_, _) => signal.TrySetResult();
      RenamedEventHandler renamed = (_, _) => signal.TrySetResult();
      watcher.Created += changed;
      watcher.Changed += changed;
      watcher.Renamed += renamed;

      var deadline = DateTimeOffset.UtcNow + timeout;
      var stableObservations = new Dictionary<
          string,
          (long Length, DateTime LastWriteUtc, int Count)>(
          StringComparer.OrdinalIgnoreCase);

      while (DateTimeOffset.UtcNow < deadline)
      {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var candidate in Directory.EnumerateFiles(
            StagingDirectory,
            "*.csv",
            SearchOption.TopDirectoryOnly))
        {
          if (IsReparsePoint(candidate))
          {
            continue;
          }

          var info = new FileInfo(candidate);
          info.Refresh();
          var previous = stableObservations.GetValueOrDefault(candidate);
          var count = previous.Length == info.Length
              && previous.LastWriteUtc == info.LastWriteTimeUtc
              ? previous.Count + 1
              : 1;
          stableObservations[candidate] = (
              info.Length,
              info.LastWriteTimeUtc,
              count);
          if (count >= 3 && CanOpenExclusively(candidate))
          {
            return candidate;
          }
        }

        signal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = deadline - DateTimeOffset.UtcNow;
        var delay = Task.Delay(
            remaining < TimeSpan.FromMilliseconds(300)
                ? remaining
                : TimeSpan.FromMilliseconds(300),
            cancellationToken);
        await Task.WhenAny(signal.Task, delay).ConfigureAwait(false);
      }

      return null;
    }

    public async Task<IProtectedPasswordCsv> ProtectAsync(
        string csvPath,
        bool allowEmpty,
        CancellationToken cancellationToken)
    {
      ThrowIfDisposed();
      var fullCsvPath = Path.GetFullPath(csvPath);
      EnsureDirectChild(fullCsvPath);
      var validation = await PasswordCsvValidator.ValidateAsync(
          fullCsvPath,
          allowEmpty,
          cancellationToken).ConfigureAwait(false);
      var encryptedPath = Path.Combine(
          StagingDirectory,
          $"{Guid.NewGuid():N}.ephemeral");
      var capture = await EphemeralProtectedPasswordCsv.CreateAsync(
          fullCsvPath,
          encryptedPath,
          validation,
          cancellationToken).ConfigureAwait(false);
      _captures.Add(capture);
      return capture;
    }

    public async ValueTask DisposeAsync()
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      foreach (var capture in _captures)
      {
        await capture.DisposeAsync().ConfigureAwait(false);
      }

      _captures.Clear();
      TryDeleteTree(StagingDirectory);
    }

    private void EnsureDirectChild(string path)
    {
      var parent = Path.GetDirectoryName(path);
      if (!string.Equals(
          parent,
          StagingDirectory,
          StringComparison.OrdinalIgnoreCase)
          || !File.Exists(path)
          || IsReparsePoint(path))
      {
        throw new PasswordCsvValidationException(
            "CSVはこのセッションの一時フォルダー直下に保存してください。");
      }
    }

    private static bool CanOpenExclusively(string path)
    {
      try
      {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        return stream.Length >= 0;
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException)
      {
        return false;
      }
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
    }
  }

  private sealed class EphemeralProtectedPasswordCsv :
      IProtectedPasswordCsv
  {
    private const int HeaderLength = 32;
    private const int ChunkSize = 256 * 1024;
    private static readonly byte[] Magic =
        [0x59, 0x42, 0x43, 0x53, 0x56, 0x54, 0x4D, 0x50];
    private readonly string _encryptedPath;
    private readonly byte[] _key;
    private bool _disposed;

    private EphemeralProtectedPasswordCsv(
        string encryptedPath,
        byte[] key,
        PasswordCsvValidationResult validation,
        string? residualPlaintextPath)
    {
      _encryptedPath = encryptedPath;
      _key = key;
      Validation = validation;
      PlaintextLength = validation.FileBytes;
      ResidualPlaintextPath = residualPlaintextPath;
    }

    public PasswordCsvValidationResult Validation { get; }

    public long PlaintextLength { get; }

    public string? ResidualPlaintextPath { get; }

    public static async Task<EphemeralProtectedPasswordCsv> CreateAsync(
        string plaintextPath,
        string encryptedPath,
        PasswordCsvValidationResult validation,
        CancellationToken cancellationToken)
    {
      var header = CreateHeader();
      var key = RandomNumberGenerator.GetBytes(32);
      var headerHash = SHA256.HashData(header);
      var noncePrefix = header.AsSpan(14, 4).ToArray();

      try
      {
        var before = new FileInfo(plaintextPath);
        before.Refresh();
        await using (var input = new FileStream(
            plaintextPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(
            encryptedPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
          await output.WriteAsync(
              header,
              cancellationToken).ConfigureAwait(false);
          using var encrypting = new ChunkedAesGcmEncryptingStream(
              output,
              key,
              headerHash,
              noncePrefix,
              ChunkSize,
              leaveOpen: true);
          await input.CopyToAsync(
              encrypting,
              128 * 1024,
              cancellationToken).ConfigureAwait(false);
          await encrypting.CompleteAsync(
              cancellationToken).ConfigureAwait(false);
          await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var after = new FileInfo(plaintextPath);
        after.Refresh();
        if (!after.Exists
            || before.Length != after.Length
            || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
        {
          throw new PasswordCsvValidationException(
              "暗号化中にCSVが変更されました。もう一度エクスポートしてください。");
        }

        string? residualPath = null;
        try
        {
          File.Delete(plaintextPath);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
          residualPath = plaintextPath;
        }

        return new EphemeralProtectedPasswordCsv(
            encryptedPath,
            key,
            validation,
            residualPath);
      }
      catch
      {
        CryptographicOperations.ZeroMemory(key);
        TryDeleteFile(encryptedPath);
        throw;
      }
      finally
      {
        CryptographicOperations.ZeroMemory(header);
        CryptographicOperations.ZeroMemory(headerHash);
        CryptographicOperations.ZeroMemory(noncePrefix);
      }
    }

    public async ValueTask<Stream> OpenReadAsync(
        CancellationToken cancellationToken)
    {
      ObjectDisposedException.ThrowIf(_disposed, this);
      var input = new FileStream(
          _encryptedPath,
          FileMode.Open,
          FileAccess.Read,
          FileShare.Read,
          128 * 1024,
          FileOptions.Asynchronous | FileOptions.SequentialScan);
      var header = new byte[HeaderLength];
      try
      {
        await input.ReadExactlyAsync(
            header,
            cancellationToken).ConfigureAwait(false);
        ValidateHeader(header);
        var headerHash = SHA256.HashData(header);
        var noncePrefix = header.AsSpan(14, 4).ToArray();
        try
        {
          return new ChunkedAesGcmDecryptingStream(
              input,
              _key,
              headerHash,
              noncePrefix,
              ChunkSize,
              leaveOpen: false);
        }
        finally
        {
          CryptographicOperations.ZeroMemory(headerHash);
          CryptographicOperations.ZeroMemory(noncePrefix);
        }
      }
      catch
      {
        await input.DisposeAsync().ConfigureAwait(false);
        throw;
      }
      finally
      {
        CryptographicOperations.ZeroMemory(header);
      }
    }

    public ValueTask DisposeAsync()
    {
      if (_disposed)
      {
        return ValueTask.CompletedTask;
      }

      _disposed = true;
      CryptographicOperations.ZeroMemory(_key);
      TryDeleteFile(_encryptedPath);
      return ValueTask.CompletedTask;
    }

    private static byte[] CreateHeader()
    {
      var header = new byte[HeaderLength];
      Magic.CopyTo(header, 0);
      BinaryPrimitives.WriteUInt16LittleEndian(
          header.AsSpan(8, 2),
          1);
      BinaryPrimitives.WriteUInt32LittleEndian(
          header.AsSpan(10, 4),
          ChunkSize);
      RandomNumberGenerator.Fill(header.AsSpan(14, 4));
      RandomNumberGenerator.Fill(header.AsSpan(18));
      return header;
    }

    private static void ValidateHeader(ReadOnlySpan<byte> header)
    {
      if (header.Length != HeaderLength
          || !header[..8].SequenceEqual(Magic)
          || BinaryPrimitives.ReadUInt16LittleEndian(
              header.Slice(8, 2)) != 1
          || BinaryPrimitives.ReadUInt32LittleEndian(
              header.Slice(10, 4)) != ChunkSize)
      {
        throw new BackupContainerIntegrityException();
      }
    }

    private static void TryDeleteFile(string path)
    {
      try
      {
        if (File.Exists(path))
        {
          File.Delete(path);
        }
      }
      catch (Exception exception) when (
          exception is IOException
          or UnauthorizedAccessException)
      {
        // キーは破棄されるため、残存しても復号できません。
      }
    }
  }
}
