using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ytec.BrowserCapsule.Infrastructure.BackupContainers;

internal sealed class ChunkedAesGcmEncryptingStream : Stream
{
  internal const int FrameHeaderLength = 16;
  internal const int TagLength = 16;
  internal const byte FinalChunkFlag = 0x01;

  private readonly Stream _output;
  private readonly AesGcm _aesGcm;
  private readonly byte[] _headerHash;
  private readonly byte[] _noncePrefix;
  private readonly byte[] _plaintextBuffer;
  private readonly byte[] _ciphertextBuffer;
  private readonly bool _leaveOpen;
  private int _bufferedCount;
  private ulong _chunkIndex;
  private bool _completed;
  private bool _disposed;

  public ChunkedAesGcmEncryptingStream(
      Stream output,
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> headerHash,
      ReadOnlySpan<byte> noncePrefix,
      int chunkSize,
      bool leaveOpen)
  {
    ArgumentNullException.ThrowIfNull(output);
    if (!output.CanWrite)
    {
      throw new ArgumentException(
          "出力ストリームへ書き込めません。",
          nameof(output));
    }

    if (key.Length != 32
        || headerHash.Length != 32
        || noncePrefix.Length != 4)
    {
      throw new ArgumentException("暗号パラメーターの長さが不正です。");
    }

    _output = output;
    _aesGcm = new AesGcm(key, TagLength);
    _headerHash = headerHash.ToArray();
    _noncePrefix = noncePrefix.ToArray();
    _plaintextBuffer = new byte[chunkSize];
    _ciphertextBuffer = new byte[chunkSize];
    _leaveOpen = leaveOpen;
  }

  public override bool CanRead => false;

  public override bool CanSeek => false;

  public override bool CanWrite => !_disposed && !_completed;

  public override long Length =>
      throw new NotSupportedException();

  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  public override void Flush()
  {
    ThrowIfDisposed();
    _output.Flush();
  }

  public override Task FlushAsync(CancellationToken cancellationToken)
  {
    ThrowIfDisposed();
    return _output.FlushAsync(cancellationToken);
  }

  public override void Write(
      byte[] buffer,
      int offset,
      int count)
  {
    ValidateBufferArguments(buffer, offset, count);
    Write(buffer.AsSpan(offset, count));
  }

  public override void Write(ReadOnlySpan<byte> buffer)
  {
    ThrowIfCannotWrite();

    while (!buffer.IsEmpty)
    {
      var copyLength = Math.Min(
          _plaintextBuffer.Length - _bufferedCount,
          buffer.Length);
      buffer[..copyLength].CopyTo(
          _plaintextBuffer.AsSpan(_bufferedCount));
      _bufferedCount += copyLength;
      buffer = buffer[copyLength..];

      if (_bufferedCount == _plaintextBuffer.Length)
      {
        WriteChunk(isFinal: false);
      }
    }
  }

  public override async ValueTask WriteAsync(
      ReadOnlyMemory<byte> buffer,
      CancellationToken cancellationToken = default)
  {
    ThrowIfCannotWrite();

    while (!buffer.IsEmpty)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var copyLength = Math.Min(
          _plaintextBuffer.Length - _bufferedCount,
          buffer.Length);
      buffer[..copyLength].CopyTo(
          _plaintextBuffer.AsMemory(_bufferedCount));
      _bufferedCount += copyLength;
      buffer = buffer[copyLength..];

      if (_bufferedCount == _plaintextBuffer.Length)
      {
        await WriteChunkAsync(
            isFinal: false,
            cancellationToken).ConfigureAwait(false);
      }
    }
  }

  public async Task CompleteAsync(CancellationToken cancellationToken)
  {
    ThrowIfDisposed();
    if (_completed)
    {
      return;
    }

    await WriteChunkAsync(
        isFinal: true,
        cancellationToken).ConfigureAwait(false);
    await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    _completed = true;
  }

  public override int Read(
      byte[] buffer,
      int offset,
      int count)
  {
    throw new NotSupportedException();
  }

  public override long Seek(long offset, SeekOrigin origin)
  {
    throw new NotSupportedException();
  }

  public override void SetLength(long value)
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
          _output.Dispose();
        }
      }

      CryptographicOperations.ZeroMemory(_plaintextBuffer);
      CryptographicOperations.ZeroMemory(_ciphertextBuffer);
      _disposed = true;
    }

    base.Dispose(disposing);
  }

  private void WriteChunk(bool isFinal)
  {
    var frameHeader = CreateFrameHeader(isFinal);
    Span<byte> nonce = stackalloc byte[12];
    CreateNonce(nonce);
    Span<byte> tag = stackalloc byte[TagLength];
    Span<byte> associatedData = stackalloc byte[48];
    _headerHash.CopyTo(associatedData);
    frameHeader.CopyTo(associatedData[32..]);

    _aesGcm.Encrypt(
        nonce,
        _plaintextBuffer.AsSpan(0, _bufferedCount),
        _ciphertextBuffer.AsSpan(0, _bufferedCount),
        tag,
        associatedData);
    _output.Write(frameHeader);
    _output.Write(_ciphertextBuffer, 0, _bufferedCount);
    _output.Write(tag);
    FinishChunk();
  }

  private async Task WriteChunkAsync(
      bool isFinal,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var frameHeader = CreateFrameHeader(isFinal);
    var nonce = new byte[12];
    CreateNonce(nonce);
    var tag = new byte[TagLength];
    var associatedData = new byte[48];
    _headerHash.CopyTo(associatedData, 0);
    frameHeader.CopyTo(associatedData, 32);

    try
    {
      _aesGcm.Encrypt(
          nonce,
          _plaintextBuffer.AsSpan(0, _bufferedCount),
          _ciphertextBuffer.AsSpan(0, _bufferedCount),
          tag,
          associatedData);
      await _output.WriteAsync(
          frameHeader,
          cancellationToken).ConfigureAwait(false);
      await _output.WriteAsync(
          _ciphertextBuffer.AsMemory(0, _bufferedCount),
          cancellationToken).ConfigureAwait(false);
      await _output.WriteAsync(
          tag,
          cancellationToken).ConfigureAwait(false);
      FinishChunk();
    }
    finally
    {
      CryptographicOperations.ZeroMemory(nonce);
      CryptographicOperations.ZeroMemory(tag);
      CryptographicOperations.ZeroMemory(associatedData);
    }
  }

  private byte[] CreateFrameHeader(bool isFinal)
  {
    if (_chunkIndex == ulong.MaxValue)
    {
      throw new InvalidOperationException(
          "暗号チャンク番号の上限に達しました。");
    }

    var frameHeader = new byte[FrameHeaderLength];
    BinaryPrimitives.WriteUInt64LittleEndian(
        frameHeader.AsSpan(0, 8),
        _chunkIndex);
    BinaryPrimitives.WriteUInt32LittleEndian(
        frameHeader.AsSpan(8, 4),
        checked((uint)_bufferedCount));
    frameHeader[12] = isFinal ? FinalChunkFlag : (byte)0;
    return frameHeader;
  }

  private void CreateNonce(Span<byte> nonce)
  {
    _noncePrefix.CopyTo(nonce);
    BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], _chunkIndex);
  }

  private void FinishChunk()
  {
    CryptographicOperations.ZeroMemory(
        _plaintextBuffer.AsSpan(0, _bufferedCount));
    _bufferedCount = 0;
    _chunkIndex++;
  }

  private void ThrowIfCannotWrite()
  {
    ThrowIfDisposed();
    if (_completed)
    {
      throw new InvalidOperationException(
          "完成済みの暗号ストリームへは書き込めません。");
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }
}
