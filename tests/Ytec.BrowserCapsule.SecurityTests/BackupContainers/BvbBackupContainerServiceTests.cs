using System.Buffers.Binary;
using Ytec.BrowserCapsule.Domain.BackupContainers;
using Ytec.BrowserCapsule.Infrastructure.BackupContainers;

namespace Ytec.BrowserCapsule.SecurityTests.BackupContainers;

public sealed class BvbBackupContainerServiceTests
{
  private const int TestChunkSize = 64 * 1024;
  private const int OuterHeaderLength = 82;
  private static readonly char[] CorrectPassphrase =
      "correct horse battery staple"u8
          .ToArray()
          .Select(value => (char)value)
          .ToArray();
  private static readonly char[] WrongPassphrase =
      "this is the wrong passphrase"u8
          .ToArray()
          .Select(value => (char)value)
          .ToArray();

  [Fact]
  public async Task RecoveryKeyFileRoundTripsV2AndDoesNotNeedPassphrase()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var destination = temporary.GetPath("v2-roundtrip.bvb");
    var keyPath = temporary.GetPath("v2-roundtrip.ybckey");
    var request = CreateRequest(
        destination,
        [CreateMemoryEntry(
            "profiles/chrome/p1/Bookmarks",
            "v2 secret"u8.ToArray())]);

    var writeResult = await service.CreateAsync(
        request,
        keyPath,
        CancellationToken.None);
    var header = await service.ReadHeaderAsync(
        destination,
        CancellationToken.None);
    var verification = await service.VerifyAsync(
        destination,
        BackupContainerCredential.FromRecoveryKeyFile(keyPath),
        CancellationToken.None);

    Assert.Equal(2, header.FormatVersion);
    Assert.Equal(2, verification.Manifest.FormatVersion);
    Assert.Equal(
        BackupContainerCredentialKind.RecoveryKeyFile,
        header.CredentialKind);
    Assert.Equal(keyPath, writeResult.RecoveryKeyPath);
    Assert.True(File.Exists(destination));
    Assert.True(File.Exists(keyPath));
    Assert.Equal(1, verification.EntryCount);
    Assert.Equal(request.BackupId, verification.Manifest.BackupId);
  }

  [Fact]
  public async Task RecoveryKeyFromAnotherBackupIsRejected()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var first = await service.CreateAsync(
        CreateRequest(temporary.GetPath("first-v2.bvb"), []),
        temporary.GetPath("first-v2.ybckey"),
        CancellationToken.None);
    var secondRequest = CreateRequest(
        temporary.GetPath("second-v2.bvb"),
        []) with
    {
      BackupId = Guid.NewGuid(),
    };
    var second = await service.CreateAsync(
        secondRequest,
        temporary.GetPath("second-v2.ybckey"),
        CancellationToken.None);

    await Assert.ThrowsAsync<BackupContainerIntegrityException>(
        () => service.VerifyAsync(
            first.FinalPath,
            BackupContainerCredential.FromRecoveryKeyFile(
                second.RecoveryKeyPath!),
            CancellationToken.None));
  }

  [Fact]
  public async Task TamperedRecoveryKeyIsRejected()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var keyPath = temporary.GetPath("tampered.ybckey");
    var result = await service.CreateAsync(
        CreateRequest(temporary.GetPath("tampered.bvb"), []),
        keyPath,
        CancellationToken.None);
    var keyBytes = await File.ReadAllBytesAsync(keyPath);
    keyBytes[30] ^= 0x20;
    await File.WriteAllBytesAsync(keyPath, keyBytes);

    await Assert.ThrowsAsync<BackupContainerIntegrityException>(
        () => service.VerifyAsync(
            result.FinalPath,
            BackupContainerCredential.FromRecoveryKeyFile(keyPath),
            CancellationToken.None));
  }

  [Fact]
  public async Task CorrectPassphraseRoundTripsManifestAndHashes()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var first = "こんにちは、Capsule"u8.ToArray();
    var request = CreateRequest(
        temporary.GetPath("roundtrip.bvb"),
        [
          CreateMemoryEntry("profiles/chrome/p1/Bookmarks", first),
          CreateMemoryEntry("profiles/chrome/p1/Empty", []),
        ]);

    var writeResult = await service.CreateAsync(
        request,
        CorrectPassphrase,
        CancellationToken.None);
    var verification = await service.VerifyAsync(
        writeResult.FinalPath,
        CorrectPassphrase,
        CancellationToken.None);

    Assert.True(File.Exists(writeResult.FinalPath));
    Assert.False(File.Exists(writeResult.FinalPath + ".partial"));
    Assert.Equal(2, verification.EntryCount);
    Assert.Equal(request.BackupId, verification.Manifest.BackupId);
    var files = Assert.Single(verification.Manifest.Profiles).Files;
    Assert.Equal(2, files.Count);
    Assert.Equal(
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(first)),
        files.Single(file => file.EntryPath.EndsWith(
            "Bookmarks",
            StringComparison.Ordinal)).Sha256);
  }

  [Fact]
  public async Task PassphraseShorterThanEightCharactersIsRejected()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var request = CreateRequest(
        temporary.GetPath("short-passphrase.bvb"),
        [CreateMemoryEntry("profiles/chrome/p1/Preferences", [1])]);

    var exception = await Assert.ThrowsAsync<ArgumentException>(
        () => service.CreateAsync(
            request,
            "1234567".ToCharArray(),
            CancellationToken.None));

    Assert.Contains("8文字以上", exception.Message, StringComparison.Ordinal);
    Assert.False(File.Exists(request.DestinationPath));
  }

  [Fact]
  public async Task WrongPassphraseIsRejectedWithoutDistinguishingCause()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var request = CreateRequest(
        temporary.GetPath("wrong-passphrase.bvb"),
        [CreateMemoryEntry("profiles/edge/p1/Preferences", [1, 2, 3])]);
    var result = await service.CreateAsync(
        request,
        CorrectPassphrase,
        CancellationToken.None);

    var exception = await Assert.ThrowsAsync<BackupContainerIntegrityException>(
        () => service.VerifyAsync(
            result.FinalPath,
            WrongPassphrase,
            CancellationToken.None));

    Assert.Equal(
        "バックアップの復元キー、パスフレーズ、または完全性を確認できません。",
        exception.Message);
  }

  [Fact]
  public async Task HeaderCiphertextTagOrderDuplicationMissingAndTruncationAreRejected()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var data = Enumerable
        .Range(0, TestChunkSize + 4096)
        .Select(index => (byte)(index % 251))
        .ToArray();
    var request = CreateRequest(
        temporary.GetPath("tamper-source.bvb"),
        [CreateMemoryEntry("profiles/firefox/p1/prefs.js", data)]);
    var result = await service.CreateAsync(
        request,
        CorrectPassphrase,
        CancellationToken.None);
    var original = await File.ReadAllBytesAsync(result.FinalPath);
    var frames = ReadFrames(original);
    Assert.True(frames.Count >= 2);

    var variants = new Dictionary<string, byte[]>
    {
      ["header"] = MutateByte(original, 20),
      ["ciphertext"] = MutateByte(
          original,
          frames[0].Offset + 16),
      ["tag"] = MutateByte(
          original,
          frames[0].Offset + frames[0].Length - 1),
      ["truncated"] = original[..^7],
      ["reordered"] = ReorderFirstTwoFrames(original, frames),
      ["duplicated"] = DuplicateFirstFrame(original, frames),
      ["missing"] = RemoveFirstFrame(original, frames),
      ["trailing"] = [.. original, 0x7F],
    };

    foreach (var variant in variants)
    {
      var path = temporary.GetPath($"{variant.Key}.bvb");
      await File.WriteAllBytesAsync(path, variant.Value);

      await Assert.ThrowsAsync<BackupContainerIntegrityException>(
          () => service.VerifyAsync(
              path,
              CorrectPassphrase,
              CancellationToken.None));
    }
  }

  [Fact]
  public async Task EmptyBackupIsAuthenticatedAndVerifiable()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var request = CreateRequest(
        temporary.GetPath("empty.bvb"),
        []);

    var result = await service.CreateAsync(
        request,
        CorrectPassphrase,
        CancellationToken.None);

    Assert.Equal(0, result.Verification.EntryCount);
    Assert.Empty(
        Assert.Single(result.Verification.Manifest.Profiles).Files);
  }

  [Fact]
  public async Task LargeGeneratedStreamUsesMultipleUniqueMonotonicNonces()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    const long generatedLength = 8L * 1024 * 1024 + 123;
    var entry = new BackupContainerEntrySource(
        "p1",
        "profiles/chrome/p1/generated.bin",
        generatedLength,
        _ => ValueTask.FromResult<Stream>(
            new PatternStream(generatedLength)));
    var request = CreateRequest(
        temporary.GetPath("large.bvb"),
        [entry]);

    var result = await service.CreateAsync(
        request,
        CorrectPassphrase,
        CancellationToken.None);
    var bytes = await File.ReadAllBytesAsync(result.FinalPath);
    var frames = ReadFrames(bytes);
    var indexes = frames.Select(frame => frame.Index).ToArray();

    Assert.True(frames.Count > 100);
    Assert.Equal(
        Enumerable.Range(0, frames.Count).Select(value => (ulong)value),
        indexes);
    Assert.Equal(indexes.Length, indexes.Distinct().Count());
    Assert.Equal(generatedLength, Assert.Single(
        Assert.Single(result.Verification.Manifest.Profiles).Files).Length);
  }

  [Fact]
  public async Task CancellationLeavesNeitherFinalNorPartialFile()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var destination = temporary.GetPath("cancelled.bvb");
    var request = CreateRequest(
        destination,
        [CreateMemoryEntry("profiles/chrome/p1/file.bin", [1, 2, 3])]);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => service.CreateAsync(
            request,
            CorrectPassphrase,
            cancellation.Token));

    Assert.False(File.Exists(destination));
    Assert.False(File.Exists(destination + ".partial"));
  }

  [Fact]
  public async Task DifferentBackupsUseDifferentSaltAndNoncePrefix()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var first = await service.CreateAsync(
        CreateRequest(temporary.GetPath("first.bvb"), []),
        CorrectPassphrase,
        CancellationToken.None);
    var secondRequest = CreateRequest(
        temporary.GetPath("second.bvb"),
        []) with
    {
      BackupId = Guid.NewGuid(),
    };
    var second = await service.CreateAsync(
        secondRequest,
        CorrectPassphrase,
        CancellationToken.None);
    var firstBytes = await File.ReadAllBytesAsync(first.FinalPath);
    var secondBytes = await File.ReadAllBytesAsync(second.FinalPath);

    Assert.NotEqual(
        firstBytes.AsSpan(16, 32).ToArray(),
        secondBytes.AsSpan(16, 32).ToArray());
    Assert.NotEqual(
        firstBytes.AsSpan(56, 4).ToArray(),
        secondBytes.AsSpan(56, 4).ToArray());
  }

  [Fact]
  public async Task SourceLengthChangeFailsAndDeletesPartial()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var destination = temporary.GetPath("changed.bvb");
    var entry = new BackupContainerEntrySource(
        "p1",
        "profiles/chrome/p1/changing.bin",
        Length: 2,
        _ => ValueTask.FromResult<Stream>(
            new MemoryStream([1, 2, 3], writable: false)));
    var request = CreateRequest(destination, [entry]);

    await Assert.ThrowsAsync<BackupContainerException>(
        () => service.CreateAsync(
            request,
            CorrectPassphrase,
            CancellationToken.None));

    Assert.False(File.Exists(destination));
    Assert.False(File.Exists(destination + ".partial"));
  }

  [Fact]
  public async Task VerifiedExtractionWritesOnlyInsideEmptyDestination()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var content = "復元ステージング"u8.ToArray();
    var request = CreateRequest(
        temporary.GetPath("extract.bvb"),
        [
          CreateMemoryEntry(
              "profiles/chrome/p1/profile/sub/Bookmarks",
              content,
              "bookmarks"),
        ]);
    var result = await service.CreateAsync(
        request,
        CorrectPassphrase,
        CancellationToken.None);
    var extractionRoot = temporary.GetPath("extracted");
    Directory.CreateDirectory(extractionRoot);

    var extraction = await service.ExtractVerifiedAsync(
        result.FinalPath,
        CorrectPassphrase,
        extractionRoot,
        CancellationToken.None);

    var extracted = Assert.Single(extraction.Files);
    Assert.Equal("bookmarks", extracted.Component);
    Assert.Equal(content, await File.ReadAllBytesAsync(extracted.AbsolutePath));
    Assert.StartsWith(
        Path.GetFullPath(extractionRoot) + Path.DirectorySeparatorChar,
        Path.GetFullPath(extracted.AbsolutePath),
        StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task SelectedExtractionAuthenticatesAllButWritesOnlyRequestedEntries()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    const string selectedPath =
        "profiles/chrome/p1/passwords/passwords.csv";
    const string omittedPath =
        "profiles/chrome/p1/profile/Bookmarks";
    var result = await service.CreateAsync(
        CreateRequest(
            temporary.GetPath("selected-extract.bvb"),
            [
              CreateMemoryEntry(
                  selectedPath,
                  "url,username,password\r\nhttps://example.test,u,p\r\n"u8
                      .ToArray(),
                  "passwordCsv"),
              CreateMemoryEntry(
                  omittedPath,
                  "{}"u8.ToArray(),
                  "bookmarks"),
            ]),
        CorrectPassphrase,
        CancellationToken.None);
    var extractionRoot = temporary.GetPath("selected");
    Directory.CreateDirectory(extractionRoot);

    var extraction = await service.ExtractSelectedVerifiedAsync(
        result.FinalPath,
        CorrectPassphrase,
        extractionRoot,
        new HashSet<string>(StringComparer.Ordinal)
        {
          selectedPath,
        },
        CancellationToken.None);

    var extracted = Assert.Single(extraction.Files);
    Assert.Equal(selectedPath, extracted.EntryPath);
    Assert.Equal("passwordCsv", extracted.Component);
    Assert.True(File.Exists(extracted.AbsolutePath));
    Assert.False(File.Exists(
        Path.Combine(
            extractionRoot,
            omittedPath.Replace('/', Path.DirectorySeparatorChar))));
    Assert.Equal(2, extraction.Verification.EntryCount);
  }

  [Fact]
  public async Task ExtractionRejectsNonEmptyDestinationBeforeDecrypting()
  {
    using var temporary = new TemporaryDirectory();
    var service = CreateService();
    var result = await service.CreateAsync(
        CreateRequest(temporary.GetPath("non-empty.bvb"), []),
        CorrectPassphrase,
        CancellationToken.None);
    var extractionRoot = temporary.GetPath("non-empty");
    Directory.CreateDirectory(extractionRoot);
    await File.WriteAllTextAsync(
        Path.Combine(extractionRoot, "keep.txt"),
        "keep");

    await Assert.ThrowsAsync<BackupContainerException>(
        () => service.ExtractVerifiedAsync(
            result.FinalPath,
            CorrectPassphrase,
            extractionRoot,
            CancellationToken.None));

    Assert.Equal(
        "keep",
        await File.ReadAllTextAsync(
            Path.Combine(extractionRoot, "keep.txt")));
  }

  private static BvbBackupContainerService CreateService()
  {
    return new BvbBackupContainerService(
        new BvbContainerSecurityOptions(
            BvbContainerSecurityOptions.MinimumPbkdf2Iterations,
            TestChunkSize,
            new Version(0, 2, 0)));
  }

  private static BackupContainerRequest CreateRequest(
      string destination,
      IReadOnlyList<BackupContainerEntrySource> entries)
  {
    return new BackupContainerRequest(
        destination,
        AppVersion: "0.2.0",
        EnvironmentFingerprint: "synthetic-test-environment",
        Profiles:
        [
          new BackupProfileDescriptor(
              "p1",
              "chrome",
              "Profile 1",
              "合成テスト",
              ["settings"],
              PasswordCsv: null,
              Warnings: []),
        ],
        entries,
        BackupId: Guid.Parse("ee0979d4-f936-4233-944f-546404451e8c"),
        CreatedUtc: DateTimeOffset.Parse(
            "2026-07-24T00:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture));
  }

  private static BackupContainerEntrySource CreateMemoryEntry(
      string path,
      byte[] bytes,
      string? component = null)
  {
    return new BackupContainerEntrySource(
        "p1",
        path,
        bytes.LongLength,
        _ => ValueTask.FromResult<Stream>(
            new MemoryStream(bytes, writable: false)),
        component);
  }

  private static byte[] MutateByte(byte[] source, int offset)
  {
    var result = source.ToArray();
    result[offset] ^= 0x40;
    return result;
  }

  private static List<FrameLocation> ReadFrames(byte[] container)
  {
    var frames = new List<FrameLocation>();
    var offset = OuterHeaderLength;

    while (offset < container.Length)
    {
      var index = BinaryPrimitives.ReadUInt64LittleEndian(
          container.AsSpan(offset, 8));
      var plaintextLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
          container.AsSpan(offset + 8, 4)));
      var frameLength = 16 + plaintextLength + 16;
      frames.Add(new FrameLocation(offset, frameLength, index));
      var isFinal = container[offset + 12] == 1;
      offset += frameLength;
      if (isFinal)
      {
        break;
      }
    }

    Assert.Equal(container.Length, offset);
    return frames;
  }

  private static byte[] ReorderFirstTwoFrames(
      byte[] source,
      IReadOnlyList<FrameLocation> frames)
  {
    using var output = new MemoryStream(source.Length);
    output.Write(source, 0, OuterHeaderLength);
    output.Write(
        source,
        frames[1].Offset,
        frames[1].Length);
    output.Write(
        source,
        frames[0].Offset,
        frames[0].Length);
    var remainingOffset = frames[1].Offset + frames[1].Length;
    output.Write(
        source,
        remainingOffset,
        source.Length - remainingOffset);
    return output.ToArray();
  }

  private static byte[] DuplicateFirstFrame(
      byte[] source,
      IReadOnlyList<FrameLocation> frames)
  {
    using var output = new MemoryStream(
        source.Length + frames[0].Length);
    output.Write(source, 0, OuterHeaderLength);
    output.Write(
        source,
        frames[0].Offset,
        frames[0].Length);
    output.Write(
        source,
        frames[0].Offset,
        source.Length - frames[0].Offset);
    return output.ToArray();
  }

  private static byte[] RemoveFirstFrame(
      byte[] source,
      IReadOnlyList<FrameLocation> frames)
  {
    using var output = new MemoryStream(
        source.Length - frames[0].Length);
    output.Write(source, 0, OuterHeaderLength);
    var remainingOffset = frames[0].Offset + frames[0].Length;
    output.Write(
        source,
        remainingOffset,
        source.Length - remainingOffset);
    return output.ToArray();
  }

  private sealed record FrameLocation(
      int Offset,
      int Length,
      ulong Index);

  private sealed class PatternStream(long length) : Stream
  {
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
      get => _position;
      set => throw new NotSupportedException();
    }

    public override int Read(
        byte[] buffer,
        int offset,
        int count)
    {
      ValidateBufferArguments(buffer, offset, count);
      var readLength = (int)Math.Min(count, length - _position);
      for (var index = 0; index < readLength; index++)
      {
        buffer[offset + index] = (byte)((_position + index) % 251);
      }

      _position += readLength;
      return readLength;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var readLength = (int)Math.Min(buffer.Length, length - _position);
      var span = buffer.Span;
      for (var index = 0; index < readLength; index++)
      {
        span[index] = (byte)((_position + index) % 251);
      }

      _position += readLength;
      return ValueTask.FromResult(readLength);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
      throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
      throw new NotSupportedException();
    }

    public override void Write(
        byte[] buffer,
        int offset,
        int count)
    {
      throw new NotSupportedException();
    }
  }
}
