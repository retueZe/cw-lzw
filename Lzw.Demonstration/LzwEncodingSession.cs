using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Lzw.Demonstration;
public class LzwEncodingSession : IAsyncDisposable {
    AutoResetEvent _unpaused = new(false);
    CancellationTokenSource _cts = new();
    bool _isClosed;

    public ArrayBufferWriter<char> DecodingOutput { get; }
    public ArrayBufferWriter<byte> EncodingOutput { get; }
    public ReadOnlyMemory<char> Input { get; }
    public LzwEncoder Encoder { get; }
    public Task<bool> Task { get; }
    public CancellationToken CancellationToken => _cts.Token;

    public event EventHandler<LzwEncodingPausedEventArgs>? Paused;

    public LzwEncodingSession(ReadOnlyMemory<char> input) {
        DecodingOutput = new();
        EncodingOutput = new();
        Input = input;
        Encoder = new LzwEncoder(Input.Span.ToArray());
        Task = Task<bool>.Factory.StartNew(
            EncodingTaskAction,
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    protected virtual void ThrowIfClosed() {
        if (_isClosed) throw new ObjectDisposedException(null);
    }
    protected virtual void OnPaused(object entry) {
        _cts.Token.ThrowIfCancellationRequested();
        Paused?.Invoke(this, new(entry));
        _unpaused.WaitOne();
    }
    protected virtual ValueTask CloseAsync(bool @explicit) {
        if (_isClosed) return ValueTask.CompletedTask;

        _isClosed = true;

        var task = new ValueTask(Task.ContinueWith(task => {
            if (@explicit) {
                GC.SuppressFinalize(this);
                _unpaused.Close();
                _cts.Dispose();
            }

            _unpaused = null!;
            _cts = null!;
        }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent));
        _cts.Cancel();
        _unpaused.Set();

        return task;
    }
    public ValueTask CloseAsync() => CloseAsync(true);
    ValueTask IAsyncDisposable.DisposeAsync() => CloseAsync();
    bool EncodingTaskAction() {
        _cts.Token.ThrowIfCancellationRequested();
        _unpaused.WaitOne();
        _cts.Token.ThrowIfCancellationRequested();

        var progress = new Progress(this);

        if (!Encoder.TryEncode(EncodingOutput, Input.Span, out var encoded, progress)) return false;

        _cts.Token.ThrowIfCancellationRequested();

        var decoder = Encoder.ToDecoder();

        if (!decoder.TryDecode(DecodingOutput, EncodingOutput.WrittenSpan[..encoded], out var _, progress)) return true;

        _cts.Token.ThrowIfCancellationRequested();

        return true;
    }
    public virtual void PostContinuationRequest() {
        ThrowIfClosed();

        _unpaused.Set();
    }
    public virtual void Cancel() {
        ThrowIfClosed();

        _cts.Cancel();
    }

    sealed class Progress : IProgress<object> {
        readonly LzwEncodingSession _target;

        public Progress(LzwEncodingSession target) => _target = target;

        public void Report(object entry) => _target.OnPaused(entry);
    }
}
