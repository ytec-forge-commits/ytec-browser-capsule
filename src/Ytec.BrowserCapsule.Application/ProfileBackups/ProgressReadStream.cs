namespace Ytec.BrowserCapsule.Application.ProfileBackups;

internal sealed class ProgressReadStream : Stream
{
  private readonly Stream _inner;
  private readonly Action<int> _onBytesRead;
  private readonly Action _onCompleted;
  private bool _completed;

  public ProgressReadStream(
      Stream inner,
      Action<int> onBytesRead,
      Action onCompleted)
  {
    ArgumentNullException.ThrowIfNull(inner);
    ArgumentNullException.ThrowIfNull(onBytesRead);
    ArgumentNullException.ThrowIfNull(onCompleted);
    _inner = inner;
    _onBytesRead = onBytesRead;
    _onCompleted = onCompleted;
  }

  public override bool CanRead => _inner.CanRead;

  public override bool CanSeek => false;

  public override bool CanWrite => false;

  public override long Length => _inner.Length;

  public override long Position
  {
    get => _inner.Position;
    set => throw new NotSupportedException();
  }

  public override int Read(
      byte[] buffer,
      int offset,
      int count)
  {
    var read = _inner.Read(buffer, offset, count);
    Report(read);
    return read;
  }

  public override async ValueTask<int> ReadAsync(
      Memory<byte> buffer,
      CancellationToken cancellationToken = default)
  {
    var read = await _inner.ReadAsync(
        buffer,
        cancellationToken).ConfigureAwait(false);
    Report(read);
    return read;
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
    if (disposing)
    {
      _inner.Dispose();
      Complete();
    }

    base.Dispose(disposing);
  }

  public override async ValueTask DisposeAsync()
  {
    await _inner.DisposeAsync().ConfigureAwait(false);
    Complete();
    await base.DisposeAsync().ConfigureAwait(false);
    GC.SuppressFinalize(this);
  }

  private void Report(int read)
  {
    if (read > 0)
    {
      _onBytesRead(read);
    }
  }

  private void Complete()
  {
    if (_completed)
    {
      return;
    }

    _completed = true;
    _onCompleted();
  }
}
