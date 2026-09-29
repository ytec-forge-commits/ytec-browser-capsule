using System.Buffers.Binary;
using System.Security.Cryptography;
using Ytec.BrowserCapsule.Domain.BackupContainers;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal sealed class ChunkedAesGcmDecryptingStream : Stream
{
  private readonly Stream _input;
  private readonly AesGcm _aesGcm;
  private readonly byte[] _headerHash;
  private readonly byte[] _noncePrefix;
  private readonly byte[] _plaintextBuffer;
  private readonly byte[] _ciphertextBuffer;
  private readonly bool _leaveOpen;
  private int _plaintextOffset;
  private int _plaintextCount;
  private ulong _expectedChunkIndex;
  private bool _finalChunkSeen;
  private bool _endVerified;
  private bool _disposed;

  public ChunkedAesGcmDecryptingStream(
      Stream input,
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> headerHash,
      ReadOnlySpan<byte> noncePrefix,
      int chunkSize,
      bool leaveOpen)
  {
    ArgumentNullException.ThrowIfNull(input);
    if (!input.CanRead)
    {
      throw new ArgumentException(
          "入力ストリームを読み取れません。",
          nameof(input));
    }

    if (key.Length != 32
        || headerHash.Length != 32
        || noncePrefix.Length != 4)
    {
      throw new ArgumentException("暗号パラメーターの長さが不正です。");
    }

    _input = input;
    _aesGcm = new AesGcm(key, ChunkedAesGcmEncryptingStream.TagLength);
    _headerHash = headerHash.ToArray();
    _noncePrefix = noncePrefix.ToArray();
    _plaintextBuffer = new byte[chunkSize];
    _ciphertextBuffer = new byte[chunkSize];
    _leaveOpen = leaveOpen;
  }

  public override bool CanRead => !_disposed;

  public override bool CanSeek => false;

  public override bool CanWrite => false;

  public override long Length =>
      throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override int Read(
      byte[] buffer,
      int offset,
      int count)
  {
    ValidateBufferArguments(buffer, offset, count);
    return Read(buffer.AsSpan(offset, count));
  }

  public override int Read(Span<byte> buffer)
  {
    ThrowIfDisposed();
    if (buffer.IsEmpty)
    {
      return 0;
    }

    while (_plaintextOffset == _plaintextCount)
    {
      if (_finalChunkSeen)
      {
        VerifyNoTrailingData();
        return 0;
      }

      LoadNextChunk();
    }

    var copyLength = Math.Min(
        buffer.Length,
        _plaintextCount - _plaintextOffset);
    _plaintextBuffer
        .AsSpan(_plaintextOffset, copyLength)
        .CopyTo(buffer);
    _plaintextOffset += copyLength;
    return copyLength;
  }

  public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    if (buffer.IsEmpty)
    {
      return 0;
    }

    while (_plaintextOffset == _plaintextCount)
    {
      if (_finalChunkSeen)
      {
        await VerifyNoTrailingDataAsync(
            cancellationToken).ConfigureAwait(false);
        return 0;
      }

      await LoadNextChunkAsync(
          cancellationToken).ConfigureAwait(false);
    }

    var copyLength = Math.Min(
        buffer.Length,
        _plaintextCount - _plaintextOffset);
    _plaintextBuffer
        .AsMemory(_plaintextOffset, copyLength)
        .CopyTo(buffer);
    _plaintextOffset += copyLength;
    return copyLength;
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

  protected override void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        _aesGcm.Dispose();
        if (!_leaveOpen)
        {
          _input.Dispose();
        }
      }

      CryptographicOperations.ZeroMemory(_plaintextBuffer);
      CryptographicOperations.ZeroMemory(_ciphertextBuffer);
      _disposed = true;
    }

    base.Dispose(disposing);
  }

  private void LoadNextChunk()
  {
    var frameHeader = new byte[ChunkedAesGcmEncryptingStream.FrameHeaderLength];
    ReadExactlyOrThrow(_input, frameHeader);
    var (plaintextLength, isFinal) = ValidateFrameHeader(frameHeader);
    ReadExactlyOrThrow(
        _input,
        _ciphertextBuffer.AsSpan(0, plaintextLength));
    Span<byte> tag =
        stackalloc byte[ChunkedAesGcmEncryptingStream.TagLength];
    ReadExactlyOrThrow(_input, tag);
    DecryptFrame(frameHeader, plaintextLength, tag);
    SetLoadedChunk(plaintextLength, isFinal);
  }

  private async Task LoadNextChunkAsync(CancellationToken cancellationToken)
  {
    var frameHeader = new byte[ChunkedAesGcmEncryptingStream.FrameHeaderLength];
    await ReadExactlyOrThrowAsync(
        _input,
        frameHeader,
        cancellationToken).ConfigureAwait(false);
    var (plaintextLength, isFinal) = ValidateFrameHeader(frameHeader);
    await ReadExactlyOrThrowAsync(
        _input,
        _ciphertextBuffer.AsMemory(0, plaintextLength),
        cancellationToken).ConfigureAwait(false);
    var tag = new byte[ChunkedAesGcmEncryptingStream.TagLength];

    try
    {
      await ReadExactlyOrThrowAsync(
          _input,
          tag,
          cancellationToken).ConfigureAwait(false);
      DecryptFrame(frameHeader, plaintextLength, tag);
      SetLoadedChunk(plaintextLength, isFinal);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(tag);
    }
  }

  private (int PlaintextLength, bool IsFinal) ValidateFrameHeader(
      ReadOnlySpan<byte> frameHeader)
  {
    var chunkIndex = BinaryPrimitives.ReadUInt64LittleEndian(
        frameHeader[..8]);
    var plaintextLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        frameHeader[8..12]));
    var flags = frameHeader[12];

    if (chunkIndex != _expectedChunkIndex
        || plaintextLength < 0
        || plaintextLength > _plaintextBuffer.Length
        || flags is not 0
            and not ChunkedAesGcmEncryptingStream.FinalChunkFlag
        || frameHeader[13] != 0
        || frameHeader[14] != 0
        || frameHeader[15] != 0
        || (flags == 0 && plaintextLength != _plaintextBuffer.Length))
    {
      throw new BackupContainerIntegrityException();
    }

    return (
        plaintextLength,
        flags == ChunkedAesGcmEncryptingStream.FinalChunkFlag);
  }

  private void DecryptFrame(
      ReadOnlySpan<byte> frameHeader,
      int plaintextLength,
      ReadOnlySpan<byte> tag)
  {
    Span<byte> nonce = stackalloc byte[12];
    _noncePrefix.CopyTo(nonce);
    BinaryPrimitives.WriteUInt64BigEndian(
        nonce[4..],
        _expectedChunkIndex);
    Span<byte> associatedData = stackalloc byte[48];
    _headerHash.CopyTo(associatedData);
    frameHeader.CopyTo(associatedData[32..]);

    try
    {
      _aesGcm.Decrypt(
          nonce,
          _ciphertextBuffer.AsSpan(0, plaintextLength),
          tag,
          _plaintextBuffer.AsSpan(0, plaintextLength),
          associatedData);
    }
    catch (CryptographicException exception)
    {
      throw new BackupContainerIntegrityException(exception);
    }
  }

  private void SetLoadedChunk(int plaintextLength, bool isFinal)
  {
    _plaintextOffset = 0;
    _plaintextCount = plaintextLength;
    _finalChunkSeen = isFinal;
    _expectedChunkIndex++;
  }

  private void VerifyNoTrailingData()
  {
    if (_endVerified)
    {
      return;
    }

    if (_input.ReadByte() != -1)
    {
      throw new BackupContainerIntegrityException();
    }

    _endVerified = true;
  }

  private async Task VerifyNoTrailingDataAsync(
      CancellationToken cancellationToken)
  {
    if (_endVerified)
    {
      return;
    }

    var trailingByte = new byte[1];
    if (await _input.ReadAsync(
        trailingByte,
        cancellationToken).ConfigureAwait(false) != 0)
    {
      throw new BackupContainerIntegrityException();
    }

    _endVerified = true;
  }

  private static void ReadExactlyOrThrow(
      Stream stream,
      Span<byte> buffer)
  {
    try
    {
      stream.ReadExactly(buffer);
    }
    catch (EndOfStreamException exception)
    {
      throw new BackupContainerIntegrityException(exception);
    }
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

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
