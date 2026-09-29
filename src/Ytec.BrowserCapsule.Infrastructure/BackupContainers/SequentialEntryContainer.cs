using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ytec.BrowserCapsule.Domain.BackupContainers;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal static class SequentialEntryContainer
{
  private const int CurrentFormatVersion = 1;
  private const int MaximumEntryCount = 100_000;
  private const int MaximumManifestBytes = 16 * 1024 * 1024;
  private const long MaximumIndividualFileBytes = 64L * 1024 * 1024 * 1024;
  private const long MaximumTotalFileBytes = 2L * 1024 * 1024 * 1024 * 1024;
  private const byte EntryMarker = 0x01;
  private const byte ManifestMarker = 0x02;
  private const byte EndMarker = 0xFF;
  private const int CopyBufferSize = 128 * 1024;

  private static readonly byte[] Magic =
      [0x59, 0x42, 0x56, 0x45, 0x4E, 0x54, 0x52, 0x00];
  private static readonly UTF8Encoding StrictUtf8 =
      new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
  private static readonly JsonSerializerOptions ManifestJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    MaxDepth = 64,
  };

  public static async Task<SequentialEntryWriteResult> WriteAsync(
      Stream output,
      BackupContainerRequest request,
      Guid backupId,
      DateTimeOffset createdUtc,
      int manifestFormatVersion,
      CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(output);
    ArgumentNullException.ThrowIfNull(request);
    ValidateRequest(request);
    ArgumentOutOfRangeException.ThrowIfLessThan(
        manifestFormatVersion,
        (int)BvbContainerHeaderCodec.LegacyFormatVersion);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(
        manifestFormatVersion,
        (int)BvbContainerHeaderCodec.CurrentFormatVersion);

    long plaintextBytes = 0;
    var header = new byte[16];
    Magic.CopyTo(header, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(
        header.AsSpan(8, 2),
        CurrentFormatVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(
        header.AsSpan(12, 4),
        checked((uint)request.Entries.Count));
    await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
    plaintextBytes += header.Length;

    var filesByProfile = request.Profiles.ToDictionary(
        profile => profile.BackupProfileId,
        _ => new List<BackupFileManifest>(),
        StringComparer.Ordinal);

    foreach (var entry in request.Entries)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var fileManifest = await WriteEntryAsync(
          output,
          entry,
          cancellationToken).ConfigureAwait(false);
      filesByProfile[entry.BackupProfileId].Add(fileManifest.Manifest);
      plaintextBytes = checked(
          plaintextBytes + fileManifest.PlaintextBytes);
    }

    var manifest = new BackupManifest(
        manifestFormatVersion,
        backupId,
        createdUtc,
        request.AppVersion,
        request.EnvironmentFingerprint,
        request.Profiles.Select(profile => new BackupProfileManifest(
            profile.BackupProfileId,
            profile.BrowserId,
            profile.SourceProfileId,
            profile.DisplayName,
            profile.Components,
            filesByProfile[profile.BackupProfileId],
            profile.PasswordCsv,
            profile.Warnings,
            profile.SourceWindowsUserId,
            profile.SourceWindowsUserDisplayName)).ToArray());
    var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
        manifest,
        ManifestJsonOptions);
    if (manifestBytes.Length > MaximumManifestBytes)
    {
      throw new BackupContainerException(
          "暗号化マニフェストが大きすぎます。");
    }

    try
    {
      var manifestHeader = new byte[5];
      manifestHeader[0] = ManifestMarker;
      BinaryPrimitives.WriteUInt32LittleEndian(
          manifestHeader.AsSpan(1, 4),
          checked((uint)manifestBytes.Length));
      await output.WriteAsync(
          manifestHeader,
          cancellationToken).ConfigureAwait(false);
      await output.WriteAsync(
          manifestBytes,
          cancellationToken).ConfigureAwait(false);
      await output.WriteAsync(
          new byte[] { EndMarker },
          cancellationToken).ConfigureAwait(false);
      plaintextBytes = checked(
          plaintextBytes
          + manifestHeader.Length
          + manifestBytes.Length
          + 1);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(manifestBytes);
    }

    return new SequentialEntryWriteResult(
        manifest,
        request.Entries.Count,
        plaintextBytes);
  }

  public static async Task<SequentialEntryReadResult> ReadAndVerifyAsync(
      Stream input,
      Guid expectedBackupId,
      int expectedManifestFormatVersion,
      CancellationToken cancellationToken)
  {
    return await ReadVerifyAndOptionallyExtractAsync(
        input,
        expectedBackupId,
        expectedManifestFormatVersion,
        extractionRoot: null,
        selectedEntryPaths: null,
        cancellationToken).ConfigureAwait(false);
  }

  public static async Task<SequentialEntryReadResult>
      ReadVerifyAndExtractAsync(
          Stream input,
          Guid expectedBackupId,
          int expectedManifestFormatVersion,
          string extractionRoot,
          IReadOnlySet<string>? selectedEntryPaths,
          CancellationToken cancellationToken)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(extractionRoot);
    return await ReadVerifyAndOptionallyExtractAsync(
        input,
        expectedBackupId,
        expectedManifestFormatVersion,
        Path.GetFullPath(extractionRoot),
        selectedEntryPaths,
        cancellationToken).ConfigureAwait(false);
  }

  private static async Task<SequentialEntryReadResult>
      ReadVerifyAndOptionallyExtractAsync(
          Stream input,
          Guid expectedBackupId,
          int expectedManifestFormatVersion,
          string? extractionRoot,
          IReadOnlySet<string>? selectedEntryPaths,
          CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(input);
    long plaintextBytes = 0;
    long totalFileBytes = 0;

    var header = new byte[16];
    await ReadExactlyOrThrowAsync(
        input,
        header,
        cancellationToken).ConfigureAwait(false);
    plaintextBytes += header.Length;
    if (!header.AsSpan(0, 8).SequenceEqual(Magic)
        || BinaryPrimitives.ReadUInt16LittleEndian(
            header.AsSpan(8, 2)) != CurrentFormatVersion
        || header[10] != 0
        || header[11] != 0)
    {
      throw new BackupContainerIntegrityException();
    }

    var entryCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        header.AsSpan(12, 4)));
    if (entryCount is < 0 or > MaximumEntryCount)
    {
      throw new BackupContainerIntegrityException();
    }

    var entries = new List<BackupFileManifest>(entryCount);
    var extractedFiles = new List<BackupExtractedFile>(entryCount);
    var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < entryCount; index++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var entryResult = await ReadAndVerifyEntryAsync(
          input,
          extractionRoot,
          selectedEntryPaths,
          cancellationToken).ConfigureAwait(false);
      if (!seenPaths.Add(entryResult.Manifest.EntryPath))
      {
        throw new BackupContainerIntegrityException();
      }

      entries.Add(entryResult.Manifest);
      if (entryResult.ExtractedFile is not null)
      {
        extractedFiles.Add(entryResult.ExtractedFile);
      }

      totalFileBytes = checked(
          totalFileBytes + entryResult.Manifest.Length);
      if (totalFileBytes > MaximumTotalFileBytes)
      {
        throw new BackupContainerIntegrityException();
      }

      plaintextBytes = checked(
          plaintextBytes + entryResult.PlaintextBytes);
    }

    var marker = await ReadByteOrThrowAsync(
        input,
        cancellationToken).ConfigureAwait(false);
    if (marker != ManifestMarker)
    {
      throw new BackupContainerIntegrityException();
    }

    var manifestLengthBytes = new byte[4];
    await ReadExactlyOrThrowAsync(
        input,
        manifestLengthBytes,
        cancellationToken).ConfigureAwait(false);
    var manifestLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        manifestLengthBytes));
    if (manifestLength is <= 0 or > MaximumManifestBytes)
    {
      throw new BackupContainerIntegrityException();
    }

    var manifestBytes = new byte[manifestLength];
    BackupManifest manifest;
    try
    {
      await ReadExactlyOrThrowAsync(
          input,
          manifestBytes,
          cancellationToken).ConfigureAwait(false);
      manifest = JsonSerializer.Deserialize<BackupManifest>(
          manifestBytes,
          ManifestJsonOptions)
          ?? throw new BackupContainerIntegrityException();
    }
    catch (JsonException exception)
    {
      throw new BackupContainerIntegrityException(exception);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(manifestBytes);
    }

    if (await ReadByteOrThrowAsync(
        input,
        cancellationToken).ConfigureAwait(false) != EndMarker)
    {
      throw new BackupContainerIntegrityException();
    }

    var endCheck = new byte[1];
    if (await input.ReadAsync(
        endCheck,
        cancellationToken).ConfigureAwait(false) != 0)
    {
      throw new BackupContainerIntegrityException();
    }

    plaintextBytes = checked(
        plaintextBytes + 1 + 4 + manifestLength + 1);
    ValidateManifest(
        manifest,
        expectedBackupId,
        expectedManifestFormatVersion,
        entries);
    var componentsByPath = manifest.Profiles
        .SelectMany(profile => profile.Files)
        .ToDictionary(
            file => file.EntryPath,
            file => file.Component,
            StringComparer.Ordinal);
    var verifiedExtractedFiles = extractedFiles
        .Select(file => file with
        {
          Component = componentsByPath[file.EntryPath],
        })
        .ToArray();
    if (selectedEntryPaths is not null
        && (verifiedExtractedFiles.Length != selectedEntryPaths.Count
            || verifiedExtractedFiles.Any(file =>
                !selectedEntryPaths.Contains(file.EntryPath))))
    {
      throw new BackupContainerIntegrityException();
    }

    return new SequentialEntryReadResult(
        manifest,
        entryCount,
        plaintextBytes,
        verifiedExtractedFiles);
  }

  private static async Task<EntryWriteResult> WriteEntryAsync(
      Stream output,
      BackupContainerEntrySource entry,
      CancellationToken cancellationToken)
  {
    var validatedPath = BackupEntryPathValidator.Validate(entry.EntryPath);
    if (entry.Length < 0)
    {
      throw new BackupContainerException(
          "バックアップ対象のファイル長が不正です。");
    }

    var pathBytes = StrictUtf8.GetBytes(validatedPath);
    var entryHeader = new byte[13];
    entryHeader[0] = EntryMarker;
    BinaryPrimitives.WriteUInt32LittleEndian(
        entryHeader.AsSpan(1, 4),
        checked((uint)pathBytes.Length));
    BinaryPrimitives.WriteUInt64LittleEndian(
        entryHeader.AsSpan(5, 8),
        checked((ulong)entry.Length));
    await output.WriteAsync(
        entryHeader,
        cancellationToken).ConfigureAwait(false);
    await output.WriteAsync(
        pathBytes,
        cancellationToken).ConfigureAwait(false);

    var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
    byte[] hash;
    await using var source = await entry
        .OpenReadAsync(cancellationToken)
        .ConfigureAwait(false);
    if (!source.CanRead)
    {
      throw new BackupContainerException(
          "バックアップ対象を読み取れません。");
    }

    using (var hasher = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256))
    {
      try
      {
        long remaining = entry.Length;
        while (remaining > 0)
        {
          cancellationToken.ThrowIfCancellationRequested();
          var requested = (int)Math.Min(buffer.Length, remaining);
          var read = await source.ReadAsync(
              buffer.AsMemory(0, requested),
              cancellationToken).ConfigureAwait(false);
          if (read == 0)
          {
            throw new BackupContainerException(
                "バックアップ中に対象ファイルが短くなりました。");
          }

          hasher.AppendData(buffer, 0, read);
          await output.WriteAsync(
              buffer.AsMemory(0, read),
              cancellationToken).ConfigureAwait(false);
          remaining -= read;
        }

        var extraByte = new byte[1];
        if (await source.ReadAsync(
            extraByte,
            cancellationToken).ConfigureAwait(false) != 0)
        {
          throw new BackupContainerException(
              "バックアップ中に対象ファイルの長さが変わりました。");
        }

        hash = hasher.GetHashAndReset();
      }
      finally
      {
        CryptographicOperations.ZeroMemory(
            buffer.AsSpan(0, buffer.Length));
        ArrayPool<byte>.Shared.Return(buffer);
      }
    }

    await output.WriteAsync(hash, cancellationToken).ConfigureAwait(false);
    var manifest = new BackupFileManifest(
        validatedPath,
        entry.Length,
        Convert.ToHexStringLower(hash),
        entry.Component);
    CryptographicOperations.ZeroMemory(hash);

    return new EntryWriteResult(
        manifest,
        checked(13L + pathBytes.Length + entry.Length + 32));
  }

  private static async Task<EntryReadResult> ReadAndVerifyEntryAsync(
      Stream input,
      string? extractionRoot,
      IReadOnlySet<string>? selectedEntryPaths,
      CancellationToken cancellationToken)
  {
    var header = new byte[13];
    await ReadExactlyOrThrowAsync(
        input,
        header,
        cancellationToken).ConfigureAwait(false);
    if (header[0] != EntryMarker)
    {
      throw new BackupContainerIntegrityException();
    }

    var pathLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        header.AsSpan(1, 4)));
    var contentLengthValue = BinaryPrimitives.ReadUInt64LittleEndian(
        header.AsSpan(5, 8));
    if (pathLength is <= 0
        or > BackupEntryPathValidator.MaximumPathUtf8Bytes
        || contentLengthValue > long.MaxValue)
    {
      throw new BackupContainerIntegrityException();
    }

    var pathBytes = new byte[pathLength];
    string entryPath;
    try
    {
      await ReadExactlyOrThrowAsync(
          input,
          pathBytes,
          cancellationToken).ConfigureAwait(false);
      entryPath = BackupEntryPathValidator.Validate(
          StrictUtf8.GetString(pathBytes));
    }
    catch (Exception exception) when (
        exception is DecoderFallbackException
        or InvalidBackupEntryPathException)
    {
      throw new BackupContainerIntegrityException(exception);
    }

    var contentLength = checked((long)contentLengthValue);
    if (contentLength > MaximumIndividualFileBytes)
    {
      throw new BackupContainerIntegrityException();
    }

    var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
    byte[] actualHash;
    var extractedPath = extractionRoot is null
        || (selectedEntryPaths is not null
            && !selectedEntryPaths.Contains(entryPath))
        ? null
        : CreateSafeExtractionFilePath(extractionRoot, entryPath);
    FileStream? extractedOutput = null;
    using (var hasher = IncrementalHash.CreateHash(
        HashAlgorithmName.SHA256))
    {
      try
      {
        if (extractedPath is not null)
        {
          extractedOutput = new FileStream(
              extractedPath,
              FileMode.CreateNew,
              FileAccess.Write,
              FileShare.None,
              CopyBufferSize,
              FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        long remaining = contentLength;
        while (remaining > 0)
        {
          cancellationToken.ThrowIfCancellationRequested();
          var readLength = (int)Math.Min(buffer.Length, remaining);
          await ReadExactlyOrThrowAsync(
              input,
              buffer.AsMemory(0, readLength),
              cancellationToken).ConfigureAwait(false);
          hasher.AppendData(buffer, 0, readLength);
          if (extractedOutput is not null)
          {
            await extractedOutput.WriteAsync(
                buffer.AsMemory(0, readLength),
                cancellationToken).ConfigureAwait(false);
          }

          remaining -= readLength;
        }

        if (extractedOutput is not null)
        {
          await extractedOutput.FlushAsync(cancellationToken)
              .ConfigureAwait(false);
        }

        actualHash = hasher.GetHashAndReset();
      }
      finally
      {
        if (extractedOutput is not null)
        {
          await extractedOutput.DisposeAsync().ConfigureAwait(false);
        }

        CryptographicOperations.ZeroMemory(
            buffer.AsSpan(0, buffer.Length));
        ArrayPool<byte>.Shared.Return(buffer);
      }
    }

    var storedHash = new byte[32];
    try
    {
      await ReadExactlyOrThrowAsync(
          input,
          storedHash,
          cancellationToken).ConfigureAwait(false);
      if (!CryptographicOperations.FixedTimeEquals(
          actualHash,
          storedHash))
      {
        throw new BackupContainerIntegrityException();
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(storedHash);
    }

    var manifest = new BackupFileManifest(
        entryPath,
        contentLength,
        Convert.ToHexStringLower(actualHash));
    var extractedFile = extractedPath is null
        ? null
        : new BackupExtractedFile(
            entryPath,
            extractedPath,
            contentLength,
            manifest.Sha256,
            Component: null);
    CryptographicOperations.ZeroMemory(actualHash);
    return new EntryReadResult(
        manifest,
        checked(13L + pathLength + contentLength + 32),
        extractedFile);
  }

  private static void ValidateRequest(BackupContainerRequest request)
  {
    if (request.Entries.Count > MaximumEntryCount)
    {
      throw new BackupContainerException(
          "バックアップ対象のファイル数が上限を超えています。");
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(request.AppVersion);
    ArgumentException.ThrowIfNullOrWhiteSpace(
        request.EnvironmentFingerprint);

    var profileIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var profile in request.Profiles)
    {
      ArgumentException.ThrowIfNullOrWhiteSpace(profile.BackupProfileId);
      ArgumentException.ThrowIfNullOrWhiteSpace(profile.BrowserId);
      ArgumentException.ThrowIfNullOrWhiteSpace(profile.SourceProfileId);
      ArgumentException.ThrowIfNullOrWhiteSpace(profile.DisplayName);
      ArgumentNullException.ThrowIfNull(profile.Components);
      ArgumentNullException.ThrowIfNull(profile.Warnings);
      if (!profileIds.Add(profile.BackupProfileId))
      {
        throw new BackupContainerException(
            "バックアップ内のプロファイルIDが重複しています。");
      }
    }

    foreach (var entry in request.Entries)
    {
      ArgumentNullException.ThrowIfNull(entry.OpenReadAsync);
      if (!profileIds.Contains(entry.BackupProfileId))
      {
        throw new BackupContainerException(
            "ファイルに対応するプロファイルがありません。");
      }
    }

    BackupEntryPathValidator.ValidateNoCaseInsensitiveCollisions(
        request.Entries.Select(entry => entry.EntryPath));
  }

  private static void ValidateManifest(
      BackupManifest manifest,
      Guid expectedBackupId,
      int expectedManifestFormatVersion,
      List<BackupFileManifest> actualEntries)
  {
    if (manifest.FormatVersion != expectedManifestFormatVersion
        || manifest.BackupId != expectedBackupId
        || string.IsNullOrWhiteSpace(manifest.AppVersion)
        || string.IsNullOrWhiteSpace(manifest.EnvironmentFingerprint)
        || manifest.Profiles is null)
    {
      throw new BackupContainerIntegrityException();
    }

    var manifestFiles = new List<BackupFileManifest>();
    var profileIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var profile in manifest.Profiles)
    {
      if (profile is null
          || string.IsNullOrWhiteSpace(profile.BackupProfileId)
          || string.IsNullOrWhiteSpace(profile.BrowserId)
          || string.IsNullOrWhiteSpace(profile.SourceProfileId)
          || string.IsNullOrWhiteSpace(profile.DisplayName)
          || profile.Components is null
          || profile.Files is null
          || profile.Warnings is null
          || !profileIds.Add(profile.BackupProfileId))
      {
        throw new BackupContainerIntegrityException();
      }

      manifestFiles.AddRange(profile.Files);
    }

    if (manifestFiles.Count != actualEntries.Count)
    {
      throw new BackupContainerIntegrityException();
    }

    BackupEntryPathValidator.ValidateNoCaseInsensitiveCollisions(
        manifestFiles.Select(file => file.EntryPath));
    var actualByPath = actualEntries.ToDictionary(
        entry => entry.EntryPath,
        StringComparer.Ordinal);
    foreach (var manifestFile in manifestFiles)
    {
      if (!actualByPath.TryGetValue(
          manifestFile.EntryPath,
          out var actual)
          || manifestFile.Length != actual.Length
          || !string.Equals(
              manifestFile.Sha256,
              actual.Sha256,
              StringComparison.Ordinal))
      {
        throw new BackupContainerIntegrityException();
      }

    }
  }

  private static string CreateSafeExtractionFilePath(
      string extractionRoot,
      string entryPath)
  {
    if (!Directory.Exists(extractionRoot)
        || IsReparsePoint(extractionRoot))
    {
      throw new BackupContainerIntegrityException();
    }

    var relativePath = entryPath.Replace(
        '/',
        Path.DirectorySeparatorChar);
    var destinationPath = Path.GetFullPath(
        Path.Combine(extractionRoot, relativePath));
    var rootWithSeparator = extractionRoot.EndsWith(
        Path.DirectorySeparatorChar)
        ? extractionRoot
        : extractionRoot + Path.DirectorySeparatorChar;
    if (!destinationPath.StartsWith(
        rootWithSeparator,
        StringComparison.OrdinalIgnoreCase))
    {
      throw new BackupContainerIntegrityException();
    }

    var parent = Path.GetDirectoryName(destinationPath)
        ?? throw new BackupContainerIntegrityException();
    EnsureSafeDirectoryTree(extractionRoot, parent);
    return destinationPath;
  }

  private static void EnsureSafeDirectoryTree(
      string root,
      string directory)
  {
    var relative = Path.GetRelativePath(root, directory);
    if (relative.StartsWith("..", StringComparison.Ordinal)
        || Path.IsPathFullyQualified(relative))
    {
      throw new BackupContainerIntegrityException();
    }

    var current = root;
    foreach (var segment in relative.Split(
        Path.DirectorySeparatorChar,
        StringSplitOptions.RemoveEmptyEntries))
    {
      current = Path.Combine(current, segment);
      if (Directory.Exists(current))
      {
        if (IsReparsePoint(current))
        {
          throw new BackupContainerIntegrityException();
        }

        continue;
      }

      Directory.CreateDirectory(current);
      if (IsReparsePoint(current))
      {
        throw new BackupContainerIntegrityException();
      }
    }
  }

  private static bool IsReparsePoint(string path)
  {
    return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
  }

  private static async Task<byte> ReadByteOrThrowAsync(
      Stream input,
      CancellationToken cancellationToken)
  {
    var buffer = new byte[1];
    await ReadExactlyOrThrowAsync(
        input,
        buffer,
        cancellationToken).ConfigureAwait(false);
    return buffer[0];
  }

  private static async Task ReadExactlyOrThrowAsync(
      Stream stream,
      Memory<byte> buffer,
      CancellationToken cancellationToken)
  {
    try
    {
      await stream.ReadExactlyAsync(
          buffer,
          cancellationToken).ConfigureAwait(false);
    }
    catch (EndOfStreamException exception)
    {
      throw new BackupContainerIntegrityException(exception);
    }
  }

  internal sealed record SequentialEntryWriteResult(
      BackupManifest Manifest,
      long EntryCount,
      long PlaintextBytes);

  internal sealed record SequentialEntryReadResult(
      BackupManifest Manifest,
      long EntryCount,
      long PlaintextBytes,
      IReadOnlyList<BackupExtractedFile> ExtractedFiles);

  private sealed record EntryWriteResult(
      BackupFileManifest Manifest,
      long PlaintextBytes);

  private sealed record EntryReadResult(
      BackupFileManifest Manifest,
      long PlaintextBytes,
      BackupExtractedFile? ExtractedFile);
}
