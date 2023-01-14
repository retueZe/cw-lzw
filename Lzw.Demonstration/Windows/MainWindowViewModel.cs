using System;
using System.Buffers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Lzw.Demonstration.Windows;
public sealed class MainWindowViewModel : ObservableObject {
    public static readonly Range InitialPhraseRange = 0..1;
    readonly DispatcherTimer _updateSessionTimer = new() {
        Interval = TimeSpan.FromSeconds(1)
    };
    string _input = "";
    LzwEncodingSession? _encodingSession;
    Range _phraseRange;
    Range _bitsRange;
    int _appendedPairCount;
    int _encoded;
    int _decoded;
    bool _isDictionaryReversed;

    public ObservableCollection<PhraseBitsPair> Pairs { get; } = new(Array.Empty<PhraseBitsPair>());
    public ObservableCollection<char> DecodedMessage { get; } = new(Array.Empty<char>());
    public ObservableCollection<bool> EncodedMessage { get; } = new(Array.Empty<bool>());
    public ObservableCollection<string> EncodedMessageSegments { get; } = new(Array.Empty<string>());
    public ObservableCollection<string> DecodedMessageSegments { get; } = new(Array.Empty<string>());
    public string Input {
        get => _input;
        set {
            SetProperty(ref _input, value, nameof(Input));
            RestartUpdateSessionTimerCommand.Execute(null);
        }
    }
    public LzwEncodingSession? EncodingSession {
        get => _encodingSession;
        set {
            if (_encodingSession is not null) {
                _encodingSession.Paused -= OnEncodingSessionPaused;
                // _encodingSession.CloseAsync(); // почему не работет? хуй его знает...
            }
            if (value is not null) {
                var dispatcher = Dispatcher.CurrentDispatcher;

                value.Paused += OnEncodingSessionPaused;
                value.Task.ContinueWith(task => dispatcher.BeginInvoke(() => EncodingSession = null));

                Pairs.Clear();
                EncodedMessage.Clear();
                DecodedMessage.Clear();

                foreach (var @char in Input)
                    DecodedMessage.Add(@char);
                for (var i = 0; i < value.Encoder.Charset.Count; i++) {
                    var @char = value.Encoder.Charset[i];

                    Pairs.Add(new(new char[] { @char }, LzwEncoder.CodeToBits((ulong)i, value.Encoder.InitialCodeLength)));
                }

                PhraseRange = default;
                PhraseRange = InitialPhraseRange;
                BitsRange = default;
                EncodedMessageSegments.Clear();
                DecodedMessageSegments.Clear();
                DecodedMessageSegments.Add(Input);
                AppendedPairCount = Pairs.Count;
                IsDictionaryReversed = false;
            }

            SetProperty(ref _encodingSession, value, nameof(EncodingSession));
            MoveNextCommand.NotifyCanExecuteChanged();
        }
    }
    public Range PhraseRange {
        get => _phraseRange;
        set => SetProperty(ref _phraseRange, value, nameof(PhraseRange));
    }
    public Range BitsRange {
        get => _bitsRange;
        set => SetProperty(ref _bitsRange, value, nameof(BitsRange));
    }
    public int AppendedPairCount {
        get => _appendedPairCount;
        set => SetProperty(ref _appendedPairCount, value, nameof(AppendedPairCount));
    }
    public int Encoded {
        get => _encoded;
        set => SetProperty(ref _encoded, value, nameof(Encoded));
    }
    public int Decoded {
        get => _decoded;
        set => SetProperty(ref _decoded, value, nameof(Decoded));
    }
    public bool IsDictionaryReversed {
        get => _isDictionaryReversed;
        set => SetProperty(ref _isDictionaryReversed, value, nameof(IsDictionaryReversed));
    }
    public IAsyncRelayCommand RestartUpdateSessionTimerCommand { get; }
    public IRelayCommand MoveNextCommand { get; }

    public MainWindowViewModel() {
        RestartUpdateSessionTimerCommand = new AsyncRelayCommand(
            RestartUpdateSessionTimer,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        MoveNextCommand = new RelayCommand(
            MoveNext,
            CanMoveNext);
        PropertyChanged += OnBitsRangeChanged;
        PropertyChanged += OnPhraseRangeChanged;
    }

    Task RestartUpdateSessionTimer(CancellationToken cancellationToken) {
        var tcs = new TaskCompletionSource();
        CancellationTokenRegistration ctr = default;

        void onTick(object? sender, EventArgs args) {
            if (!tcs.TrySetResult()) return;

            _updateSessionTimer.Stop();
            _updateSessionTimer.Tick -= onTick;
            ctr.Unregister();
            EncodingSession = Input.Length == 0
                ? null
                : new LzwEncodingSession(Input.AsMemory());
        }
        void onCancelled() {
            if (!tcs.TrySetResult()) return;

            _updateSessionTimer.Stop();
            _updateSessionTimer.Tick -= onTick;
            ctr.Unregister();
        }

        _updateSessionTimer.Tick += onTick;
        ctr = cancellationToken.Register(onCancelled);
        _updateSessionTimer.Start();

        return tcs.Task;
    }
    void MoveNext() {
        if (!CanMoveNext()) return;

        EncodingSession!.PostContinuationRequest();
    }
    bool CanMoveNext() => _encodingSession is not null &&
        !_encodingSession.Task.IsCompleted;
    void OnEncodingSessionPaused(object? sender, LzwEncodingPausedEventArgs args) {
        if (args.Entry is LzwEncodeReadActionEntry) {
            PhraseRange = PhraseRange.Start..(PhraseRange.End.GetOffset(Input.Length) + 1);
            BitsRange = default;
            AppendedPairCount = 0;
        } else if (args.Entry is LzwEncodeWriteActionEntry encodeWriteEntry) {
            if (!encodeWriteEntry.AddedPhrase.IsEmpty) {
                var pair = new PhraseBitsPair(encodeWriteEntry.AddedPhrase, encodeWriteEntry.AddedBits);

                if (!Pairs.Contains(pair)) {
                    Pairs.Add(pair);
                    AppendedPairCount = 1;
                }
            }
            
            foreach (var bit in encodeWriteEntry.WrittenBits.Span)
                EncodedMessage.Add(bit);

            PhraseRange = (PhraseRange.End.GetOffset(Input.Length) - 1)..PhraseRange.End;
            BitsRange = default;
            BitsRange = ^encodeWriteEntry.WrittenBits.Length..;
        } else if (args.Entry is LzwEncodeCompleteActionEntry encodeCompleteEntry) {
            IsDictionaryReversed = true;
            PhraseRange = default;
            BitsRange = default;
            DecodedMessageSegments.Clear();
            DecodedMessage.Clear();

            while (Pairs.Count > EncodingSession!.Encoder.Charset.Count)
                Pairs.RemoveAt(Pairs.Count - 1);

            AppendedPairCount = EncodingSession.Encoder.Charset.Count;
            Encoded = encodeCompleteEntry.Encoded;
        } else if (args.Entry is LzwDecodeReadActionEntry decodeReadEntry) {
            BitsRange = BitsRange.End.GetOffset(EncodedMessage.Count) + decodeReadEntry.Bits.Length <= EncodedMessage.Count
                ? BitsRange.End..(BitsRange.End.GetOffset(EncodedMessage.Count) + decodeReadEntry.Bits.Length)
                : default;
            PhraseRange = default;
            AppendedPairCount = 0;
        } else if (args.Entry is LzwDecodeWriteActionEntry decodeWriteEntry) {
            if (!decodeWriteEntry.AddedPhrase.IsEmpty) {
                var pair = new PhraseBitsPair(decodeWriteEntry.AddedPhrase, decodeWriteEntry.AddedBits);

                if (!Pairs.Contains(pair)) {
                    Pairs.Add(pair);
                    AppendedPairCount = 1;
                }
            }
            
            foreach (var @char in decodeWriteEntry.WrittenPhrase.Span)
                DecodedMessage.Add(@char);

            PhraseRange = ^decodeWriteEntry.WrittenPhrase.Length..;
        } else if (args.Entry is LzwDecodeCompleteActionEntry decodeCompleteEntry) {
            PhraseRange = default;
            BitsRange = default;
            AppendedPairCount = 0;
            Decoded = (int)decodeCompleteEntry.Decoded;
        }
    }
    void OnBitsRangeChanged(object? sender, PropertyChangedEventArgs args) {
        if (args.PropertyName != nameof(BitsRange)) return;

        var builder = new StringBuilder();
        var chunkLength = 0;
        EncodedMessageSegments.Clear();
        var start = BitsRange.Start.GetOffset(EncodedMessage.Count);
        var end = BitsRange.End.GetOffset(EncodedMessage.Count);

        for (var i = 0; i < start; i++) {
            var bit = EncodedMessage[i];

            if (chunkLength >= 4) {
                builder.Append('\xa0');
                chunkLength = 0;
            }

            builder.Append(bit ? '1' : '0');
            chunkLength++;
        }

        EncodedMessageSegments.Add(builder.ToString());
        builder.Clear();

        for (var i = start; i < end; i++) {
            var bit = EncodedMessage[i];

            if (chunkLength >= 4) {
                builder.Append('\xa0');
                chunkLength = 0;
            }

            builder.Append(bit ? '1' : '0');
            chunkLength++;
        }

        EncodedMessageSegments.Add(builder.ToString());
        builder.Clear();
        
        for (var i = end; i < EncodedMessage.Count; i++) {
            var bit = EncodedMessage[i];

            if (chunkLength >= 4) {
                builder.Append('\xa0');
                chunkLength = 0;
            }

            builder.Append(bit ? '1' : '0');
            chunkLength++;
        }

        EncodedMessageSegments.Add(builder.ToString());
    }
    void OnPhraseRangeChanged(object? sender, PropertyChangedEventArgs args) {
        if (args.PropertyName != nameof(PhraseRange)) return;

        var builder = new StringBuilder();
        DecodedMessageSegments.Clear();
        var start = PhraseRange.Start.GetOffset(DecodedMessage.Count);
        var end = PhraseRange.End.GetOffset(DecodedMessage.Count);

        for (var i = 0; i < start; i++)
            builder.Append(DecodedMessage[i]);

        DecodedMessageSegments.Add(builder.ToString());
        builder.Clear();

        for (var i = start; i < end; i++)
            builder.Append(DecodedMessage[i]);

        DecodedMessageSegments.Add(builder.ToString());
        builder.Clear();
        
        for (var i = end; i < DecodedMessage.Count; i++)
            builder.Append(DecodedMessage[i]);

        DecodedMessageSegments.Add(builder.ToString());
    }
}
