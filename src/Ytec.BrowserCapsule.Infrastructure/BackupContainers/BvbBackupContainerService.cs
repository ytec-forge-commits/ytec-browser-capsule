using System.Security.Cryptography;
using Ytec.BrowserCapsule.Domain.BackupContainers;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

/// <summary>
/// BVB v2暗号化コンテナを作成し、BVB v1/v2を検証・展開します。
/// </summary>
public sealed class BvbBackupContainerService : IBackupContainerService
{
  private const int FileBufferSize = 128 * 1024;
  private readonly BvbContainerSecurityOptions _securityOptions;

  public BvbBackupContainerService(
      BvbContainerSecurityOptions securityOptions)
  {
    ArgumentNullException.ThrowIfNull(securityOptions);
    securityOptions.Validate();
    _securityOptions = securityOptions;
  }

  public async Task<BackupContainerHeaderInfo> ReadHeaderAsync(
      string containerPath,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
    await using var input = new FileStream(
        Path.GetFullPath(containerPath),
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        FileBufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    var encodedHeader = new byte[BvbContainerHeaderCodec.HeaderLength];
    try
    {
      await input.ReadExactlyAsync(
          encodedHeader,
          cancellationToken).ConfigureAwait(false);
      return BvbContainerHeaderCodec.Decode(encodedHeader).ToPublicInfo();
    }
    catch (OperationCanceledException) when (
        cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (BackupContainerIntegrityException)
    {
      throw;
    }
    catch (Exception exception) when (
        exception is EndOfStreamException
        or IOException
        or UnauthorizedAccessException)
    {
      throw new BackupContainerIntegrityException(exception);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(encodedHeader);
    }
  }

  public async Task<BackupContainerWriteResult> CreateAsync(
      BackupContainerRequest request,
      string recoveryKeyFilePath,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    var finalPath = ValidateDestinationPath(request.DestinationPath);
    var finalKeyPath = ValidateRecoveryKeyDestinationPath(
        recoveryKeyFilePath);
    var partialPath = finalPath + ".partial";
    var partialKeyPath = finalKeyPath + ".partial";
    EnsureCreationTargetsAreAvailable(
        finalPath,
        partialPath,
        finalKeyPath,
        partialKeyPath);

    var backupId = request.BackupId ?? Guid.NewGuid();
    var createdUtc = request.CreatedUtc ?? DateTimeOffset.UtcNow;
    var key = RandomNumberGenerator.GetBytes(
        RecoveryKeyFileCodec.KeyLength);
    var keyIdentifier = SHA256.HashData(key);
    var keyFileBytes = RecoveryKeyFileCodec.Encode(backupId, key);
    var encodedHeader = BvbContainerHeaderCodec.CreateV2(
        backupId,
        keyIdentifier,
        _securityOptions,
        out var header);
    var headerHash = BvbContainerHeaderCodec.ComputeHash(encodedHeader);
    var keyMoved = false;

    try
    {
      try
      {
        await WriteRecoveryKeyPartialAsync(
            partialKeyPath,
            keyFileBytes,
            cancellationToken).ConfigureAwait(false);
        await WritePartialAsync(
            partialPath,
            request,
            backupId,
            createdUtc,
            encodedHeader,
            header,
            headerHash,
            key,
            cancellationToken).ConfigureAwait(false);

        var credential = BackupContainerCredential.FromRecoveryKeyFile(
            partialKeyPath);
        var verification = await VerifyAsync(
            partialPath,
            credential,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        File.Move(partialKeyPath, finalKeyPath, overwrite: false);
        keyMoved = true;
        File.Move(partialPath, finalPath, overwrite: false);
        return new BackupContainerWriteResult(
            finalPath,
            new FileInfo(finalPath).Length,
            verification)
        {
          RecoveryKeyPath = finalKeyPath,
        };
      }
      catch
      {
        TryDeletePartial(partialPath);
        TryDeletePartial(partialKeyPath);
        if (keyMoved)
        {
          TryDeletePartial(finalKeyPath);
        }

        throw;
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
      CryptographicOperations.ZeroMemory(keyIdentifier);
      CryptographicOperations.ZeroMemory(keyFileBytes);
      CryptographicOperations.ZeroMemory(headerHash);
      CryptographicOperations.ZeroMemory(encodedHeader);
    }
  }

  /// <summary>
  /// BVB v1作成互換経路です。製品UIは使用しません。
  /// </summary>
  public async Task<BackupContainerWriteResult> CreateAsync(
      BackupContainerRequest request,
      ReadOnlyMemory<char> passphrase,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(request);
    ValidatePassphraseForCreation(passphrase);
    var finalPath = ValidateDestinationPath(request.DestinationPath);
    var partialPath = finalPath + ".partial";

    if (File.Exists(finalPath))
    {
      throw new BackupContainerException(
          "同名の完成済みバックアップが既にあります。");
    }

    if (File.Exists(partialPath))
    {
      throw new BackupContainerException(
          "同名の途中バックアップが残っています。");
    }

    var backupId = request.BackupId ?? Guid.NewGuid();
    var createdUtc = request.CreatedUtc ?? DateTimeOffset.UtcNow;
    var encodedHeader = BvbContainerHeaderCodec.CreateLegacyV1(
        backupId,
        _securityOptions,
        out var header);
    var headerHash = BvbContainerHeaderCodec.ComputeHash(encodedHeader);
    var key = BvbKeyDerivation.DeriveKey(
        passphrase,
        header.KeyDescriptor,
        header.Pbkdf2Iterations);

    try
    {
      try
      {
        await WritePartialAsync(
            partialPath,
            request,
            backupId,
            createdUtc,
            encodedHeader,
            header,
            headerHash,
            key,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        var verification = await VerifyAsync(
            partialPath,
            passphrase,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        File.Move(partialPath, finalPath, overwrite: false);
        var containerBytes = new FileInfo(finalPath).Length;
        return new BackupContainerWriteResult(
            finalPath,
            containerBytes,
            verification);
      }
      catch
      {
        TryDeletePartial(partialPath);
        throw;
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
      CryptographicOperations.ZeroMemory(headerHash);
      CryptographicOperations.ZeroMemory(encodedHeader);
    }
  }

  public async Task<BackupContainerVerificationResult> VerifyAsync(
      string containerPath,
      ReadOnlyMemory<char> passphrase,
      CancellationToken cancellationToken)
  {
    return await VerifyAsync(
        containerPath,
        BackupContainerCredential.FromLegacyPassphrase(passphrase),
        cancellationToken).ConfigureAwait(false);
  }

  public async Task<BackupContainerVerificationResult> VerifyAsync(
      string containerPath,
      BackupContainerCredential credential,
      CancellationToken cancellationToken)
  {
    var result = await ReadContainerAsync(
        containerPath,
        credential,
        extractionRoot: null,
        selectedEntryPaths: null,
        cancellationToken).ConfigureAwait(false);
    return result.Verification;
  }

  public Task<BackupContainerExtractionResult> ExtractVerifiedAsync(
      string containerPath,
      ReadOnlyMemory<char> passphrase,
      string emptyDestinationDirectory,
      CancellationToken cancellationToken)
  {
    return ExtractVerifiedAsync(
        containerPath,
        BackupContainerCredential.FromLegacyPassphrase(passphrase),
        emptyDestinationDirectory,
        cancellationToken);
  }

  public Task<BackupContainerExtractionResult> ExtractVerifiedAsync(
      string containerPath,
      BackupContainerCredential credential,
      string emptyDestinationDirectory,
      CancellationToken cancellationToken)
  {
    var extractionRoot = ValidateExtractionRoot(
        emptyDestinationDirectory);
    return ReadContainerAsync(
        containerPath,
        credential,
        extractionRoot,
        selectedEntryPaths: null,
        cancellationToken);
  }

  public Task<BackupContainerExtractionResult>
      ExtractSelectedVerifiedAsync(
          string containerPath,
          ReadOnlyMemory<char> passphrase,
          string emptyDestinationDirectory,
      IReadOnlySet<string> selectedEntryPaths,
      CancellationToken cancellationToken)
  {
    return ExtractSelectedVerifiedAsync(
        containerPath,
        BackupContainerCredential.FromLegacyPassphrase(passphrase),
        emptyDestinationDirectory,
        selectedEntryPaths,
        cancellationToken);
  }

  public Task<BackupContainerExtractionResult>
      ExtractSelectedVerifiedAsync(
          string containerPath,
          BackupContainerCredential credential,
          string emptyDestinationDirectory,
          IReadOnlySet<string> selectedEntryPaths,
          CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(selectedEntryPaths);
    if (selectedEntryPaths.Count == 0)
    {
      throw new ArgumentException(
          "一時展開するエントリーを選択してください。",
          nameof(selectedEntryPaths));
    }

    var validatedPaths = selectedEntryPaths
        .Select(BackupEntryPathValidator.Validate)
        .ToHashSet(StringComparer.Ordinal);
    var extractionRoot = ValidateExtractionRoot(
        emptyDestinationDirectory);
    return ReadContainerAsync(
        containerPath,
        credential,
        extractionRoot,
        validatedPaths,
        cancellationToken);
  }

  private static async Task<BackupContainerExtractionResult>
      ReadContainerAsync(
      string containerPath,
      BackupContainerCredential credential,
      string? extractionRoot,
      IReadOnlySet<string>? selectedEntryPaths,
      CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
    ArgumentNullException.ThrowIfNull(credential);

    var fullPath = Path.GetFullPath(containerPath);
    await using var input = new FileStream(
        fullPath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        FileBufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    var encodedHeader = new byte[BvbContainerHeaderCodec.HeaderLength];
    byte[]? key = null;
    byte[]? headerHash = null;

    try
    {
      try
      {
        await input.ReadExactlyAsync(
            encodedHeader,
            cancellationToken).ConfigureAwait(false);
      }
      catch (EndOfStreamException exception)
      {
        throw new BackupContainerIntegrityException(exception);
      }

      var header = BvbContainerHeaderCodec.Decode(encodedHeader);
      headerHash = BvbContainerHeaderCodec.ComputeHash(encodedHeader);
      key = await ResolveKeyAsync(
          header,
          credential,
          cancellationToken).ConfigureAwait(false);

      using var decryptingStream = new ChunkedAesGcmDecryptingStream(
          input,
          key,
          headerHash,
          header.NoncePrefix,
          header.ChunkSizeBytes,
          leaveOpen: true);
      var sequentialResult = extractionRoot is null
          ? await SequentialEntryContainer
              .ReadAndVerifyAsync(
                  decryptingStream,
                  header.BackupId,
                  header.FormatVersion,
                  cancellationToken)
              .ConfigureAwait(false)
          : await SequentialEntryContainer
              .ReadVerifyAndExtractAsync(
                  decryptingStream,
                  header.BackupId,
                  header.FormatVersion,
                  extractionRoot,
                  selectedEntryPaths,
                  cancellationToken)
              .ConfigureAwait(false);

      var verification = new BackupContainerVerificationResult(
          header.ToPublicInfo(),
          sequentialResult.Manifest,
          sequentialResult.EntryCount,
          sequentialResult.PlaintextBytes);
      return new BackupContainerExtractionResult(
          verification,
          sequentialResult.ExtractedFiles);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (BackupContainerIntegrityException)
    {
      throw;
    }
    catch (Exception exception) when (
        exception is CryptographicException
        or IOException
        or UnauthorizedAccessException
        or OverflowException
        or InvalidBackupEntryPathException)
    {
      throw new BackupContainerIntegrityException(exception);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(encodedHeader);
      if (key is not null)
      {
        CryptographicOperations.ZeroMemory(key);
      }

      if (headerHash is not null)
      {
        CryptographicOperations.ZeroMemory(headerHash);
      }
    }
  }

  private static string ValidateExtractionRoot(string extractionRoot)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
    if (!Path.IsPathFullyQualified(extractionRoot)
        || extractionRoot.StartsWith(
            new string(Path.DirectorySeparatorChar, 2),
            StringComparison.Ordinal))
    {
      throw new ArgumentException(
          "展開先にはローカルの絶対パスを指定してください。",
          nameof(extractionRoot));
    }

    var fullPath = Path.GetFullPath(extractionRoot);
    if (!Directory.Exists(fullPath))
    {
      throw new DirectoryNotFoundException(
          "暗号化バックアップの一時展開先がありません。");
    }

    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0
        || Directory.EnumerateFileSystemEntries(fullPath).Any())
    {
      throw new BackupContainerException(
          "一時展開先は空の通常フォルダーである必要があります。");
    }

    return fullPath;
  }

  private static async Task<byte[]> ResolveKeyAsync(
      BvbContainerHeader header,
      BackupContainerCredential credential,
      CancellationToken cancellationToken)
  {
    if (header.CredentialKind
        == BackupContainerCredentialKind.LegacyPassphrase)
    {
      if (credential.Kind
          != BackupContainerCredentialKind.LegacyPassphrase)
      {
        throw new BackupContainerIntegrityException();
      }

      return BvbKeyDerivation.DeriveKey(
          credential.LegacyPassphrase,
          header.KeyDescriptor,
          header.Pbkdf2Iterations);
    }

    if (credential.Kind != BackupContainerCredentialKind.RecoveryKeyFile
        || string.IsNullOrWhiteSpace(credential.RecoveryKeyFilePath))
    {
      throw new BackupContainerIntegrityException();
    }

    var keyFileBytes = await File.ReadAllBytesAsync(
        credential.RecoveryKeyFilePath,
        cancellationToken).ConfigureAwait(false);
    try
    {
      var keyFile = RecoveryKeyFileCodec.Decode(keyFileBytes);
      try
      {
        var keyIdentifier = SHA256.HashData(keyFile.Key);
        try
        {
          if (keyFile.BackupId != header.BackupId
              || !CryptographicOperations.FixedTimeEquals(
                  keyIdentifier,
                  header.KeyDescriptor))
          {
            throw new BackupContainerIntegrityException();
          }

          return keyFile.Key.ToArray();
        }
        finally
        {
          CryptographicOperations.ZeroMemory(keyIdentifier);
        }
      }
      finally
      {
        CryptographicOperations.ZeroMemory(keyFile.Key);
      }
    }
    catch (BackupContainerIntegrityException)
    {
      throw;
    }
    catch (Exception exception) when (
        exception is CryptographicException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException)
    {
      throw new BackupContainerIntegrityException(exception);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(keyFileBytes);
    }
  }

  private static async Task WriteRecoveryKeyPartialAsync(
      string partialKeyPath,
      byte[] keyFileBytes,
      CancellationToken cancellationToken)
  {
    await using var output = new FileStream(
        partialKeyPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    await output.WriteAsync(
        keyFileBytes,
        cancellationToken).ConfigureAwait(false);
    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    output.Flush(flushToDisk: true);
  }

  private static async Task WritePartialAsync(
      string partialPath,
      BackupContainerRequest request,
      Guid backupId,
      DateTimeOffset createdUtc,
      byte[] encodedHeader,
      BvbContainerHeader header,
      byte[] headerHash,
      byte[] key,
      CancellationToken cancellationToken)
  {
    await using var output = new FileStream(
        partialPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        FileBufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    await output.WriteAsync(
        encodedHeader,
        cancellationToken).ConfigureAwait(false);

    using (var encryptingStream = new ChunkedAesGcmEncryptingStream(
        output,
        key,
        headerHash,
        header.NoncePrefix,
        header.ChunkSizeBytes,
        leaveOpen: true))
    {
      await SequentialEntryContainer.WriteAsync(
          encryptingStream,
          request,
          backupId,
          createdUtc,
          header.FormatVersion,
          cancellationToken).ConfigureAwait(false);
      await encryptingStream.CompleteAsync(
          cancellationToken).ConfigureAwait(false);
    }

    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    output.Flush(flushToDisk: true);
  }

  private static string ValidateDestinationPath(string destinationPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
    if (!Path.IsPathFullyQualified(destinationPath)
        || !string.Equals(
            Path.GetExtension(destinationPath),
            ".bvb",
            StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException(
          "保存先には絶対パスの.bvbファイルを指定してください。",
          nameof(destinationPath));
    }

    var fullPath = Path.GetFullPath(destinationPath);
    var directory = Path.GetDirectoryName(fullPath);
    if (directory is null || !Directory.Exists(directory))
    {
      throw new DirectoryNotFoundException(
          "バックアップ先フォルダーがありません。");
    }

    return fullPath;
  }

  private static string ValidateRecoveryKeyDestinationPath(
      string recoveryKeyFilePath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(recoveryKeyFilePath);
    if (!Path.IsPathFullyQualified(recoveryKeyFilePath)
        || !string.Equals(
            Path.GetExtension(recoveryKeyFilePath),
            ".ybckey",
            StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException(
          "復元キーの保存先には絶対パスの.ybckeyファイルを指定してください。",
          nameof(recoveryKeyFilePath));
    }

    var fullPath = Path.GetFullPath(recoveryKeyFilePath);
    var directory = Path.GetDirectoryName(fullPath);
    if (directory is null || !Directory.Exists(directory))
    {
      throw new DirectoryNotFoundException(
          "復元キーの保存先フォルダーがありません。");
    }

    return fullPath;
  }

  private static void EnsureCreationTargetsAreAvailable(
      string finalPath,
      string partialPath,
      string finalKeyPath,
      string partialKeyPath)
  {
    if (string.Equals(
        finalPath,
        finalKeyPath,
        StringComparison.OrdinalIgnoreCase))
    {
      throw new BackupContainerException(
          "バックアップと復元キーには別のファイル名を指定してください。");
    }

    if (File.Exists(finalPath) || File.Exists(finalKeyPath))
    {
      throw new BackupContainerException(
          "同名の完成済みバックアップまたは復元キーが既にあります。");
    }

    if (File.Exists(partialPath) || File.Exists(partialKeyPath))
    {
      throw new BackupContainerException(
          "同名の途中バックアップまたは復元キーが残っています。");
    }
  }

  private static void ValidatePassphraseForCreation(
      ReadOnlyMemory<char> passphrase)
  {
    if (passphrase.Length < PassphrasePolicy.MinimumLength)
    {
      throw new ArgumentException(
          $"パスフレーズは{PassphrasePolicy.MinimumLength}文字以上にしてください。",
          nameof(passphrase));
    }
  }

  private static void TryDeletePartial(string partialPath)
  {
    try
    {
      if (File.Exists(partialPath))
      {
        File.Delete(partialPath);
      }
    }
    catch (Exception exception) when (
        exception is IOException
        or UnauthorizedAccessException)
    {
      // Phase 3で再起動後清掃リストへ登録します。
    }
  }
}
