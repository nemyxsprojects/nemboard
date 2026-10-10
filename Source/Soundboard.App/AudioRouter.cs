using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace Soundboard.App;

public sealed class AudioRouter(AudioDeviceService devices) : IDisposable
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);
    private const int EndpointCreateFailed = unchecked((int)0x8889000A);
    public const string NoOutputSelectedMessage = "No audio output is selected.";
    private readonly object _sync = new();
    private readonly List<PlaybackSession> _sessions = [];
    private WasapiCapture? _microphoneCapture;
    private IWavePlayer? _microphoneOutput;
    private BufferedWaveProvider? _microphoneBuffer;
    private VolumeSampleProvider? _microphoneVolume;
    private ProcessLoopbackCapture? _browserCapture;
    private IWavePlayer? _browserOutput;
    private BufferedWaveProvider? _browserBuffer;
    private uint? _browserProcessId;
    private string? _browserCableDeviceId;
    private IWavePlayer? _testOutput;
    private IWaveProvider? _testProvider;
    private bool _disposed;
    private float _voiceVolume = 1f;
    private float _soundVolume = 1f;

    public event EventHandler<int>? ActivePlaybackCountChanged;

    public float VoiceVolume
    {
        get => _voiceVolume;
        set
        {
            _voiceVolume = Math.Clamp(value, 0f, 2f);
            if (_microphoneVolume is not null) _microphoneVolume.Volume = _voiceVolume;
        }
    }

    public float SoundVolume
    {
        get => _soundVolume;
        set
        {
            _soundVolume = Math.Clamp(value, 0f, 1f);
            lock (_sync)
            {
                foreach (var session in _sessions) session.Volume = _soundVolume;
            }
        }
    }

    public int ActivePlaybackCount
    {
        get
        {
            lock (_sync)
            {
                return _sessions.Count;
            }
        }
    }

    public static TimeSpan ReadDuration(string filePath)
    {
        using var reader = new AudioFileReader(filePath);
        return reader.TotalTime;
    }

    public void StartRouting(string microphoneDeviceId, string cableDeviceId)
    {
        StopRouting();

        try
        {
            var microphone = devices.GetDevice(microphoneDeviceId);
            var microphoneCapture = new WasapiCapture(microphone);
            var microphoneBuffer = CreateBuffer(microphoneCapture.WaveFormat);
            var microphoneVolume = new VolumeSampleProvider(microphoneBuffer.ToSampleProvider()) { Volume = _voiceVolume };
            var microphoneOutput = CreateOutputWithRetry(() => devices.GetDevice(cableDeviceId), microphoneVolume.ToWaveProvider());
            microphoneCapture.DataAvailable += (_, args) => microphoneBuffer.AddSamples(args.Buffer, 0, args.BytesRecorded);

            _microphoneCapture = microphoneCapture;
            _microphoneBuffer = microphoneBuffer;
            _microphoneVolume = microphoneVolume;
            _microphoneOutput = microphoneOutput;
            microphoneOutput.Play();
            microphoneCapture.StartRecording();

        }
        catch
        {
            StopRouting();
            throw;
        }
    }

    public void StopRouting()
    {
        _microphoneCapture?.StopRecording();
        _microphoneCapture?.Dispose();
        _microphoneOutput?.Stop();
        _microphoneOutput?.Dispose();
        _microphoneCapture = null;
        _microphoneOutput = null;
        _microphoneBuffer = null;
        _microphoneVolume = null;

        StopBrowserRouting();
    }

    public async Task StartBrowserRoutingAsync(uint browserProcessId, string cableDeviceId)
    {
        if (_browserCapture is not null && _browserProcessId == browserProcessId &&
            string.Equals(_browserCableDeviceId, cableDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopBrowserRouting();
        var capture = await ProcessLoopbackCapture.CreateAsync(browserProcessId);
        try
        {
            var buffer = CreateBuffer(capture.WaveFormat);
            var output = CreateOutputWithRetry(() => devices.GetDevice(cableDeviceId), buffer);
            capture.DataAvailable += (_, args) => buffer.AddSamples(args.Buffer, 0, args.BytesRecorded);

            _browserCapture = capture;
            _browserBuffer = buffer;
            _browserOutput = output;
            _browserProcessId = browserProcessId;
            _browserCableDeviceId = cableDeviceId;
            output.Play();
            capture.StartRecording();
        }
        catch
        {
            capture.Dispose();
            StopBrowserRouting();
            throw;
        }
    }

    public void StopBrowserRouting()
    {
        _browserCapture?.StopRecording();
        _browserCapture?.Dispose();
        _browserOutput?.Stop();
        _browserOutput?.Dispose();
        _browserCapture = null;
        _browserOutput = null;
        _browserBuffer = null;
        _browserProcessId = null;
        _browserCableDeviceId = null;
    }

    public PlaybackSession Play(string filePath, string? cableDeviceId, string? monitorDeviceId, Action? completed = null)
    {
        var outputIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(monitorDeviceId))
        {
            outputIds.Add(monitorDeviceId);
        }

        if (!string.IsNullOrWhiteSpace(cableDeviceId) &&
            !outputIds.Contains(cableDeviceId, StringComparer.OrdinalIgnoreCase))
        {
            outputIds.Add(cableDeviceId);
        }

        if (outputIds.Count == 0)
        {
            throw new InvalidOperationException(NoOutputSelectedMessage);
        }

        PlaybackSession? session = null;
        session = new PlaybackSession(filePath, outputIds, devices, _soundVolume, () =>
        {
            lock (_sync)
            {
                if (session is not null)
                {
                    _sessions.Remove(session);
                }
            }

            ActivePlaybackCountChanged?.Invoke(this, ActivePlaybackCount);
            completed?.Invoke();
        });

        lock (_sync)
        {
            _sessions.Add(session);
        }

        try
        {
            session.Start();
        }
        catch
        {
            lock (_sync)
            {
                _sessions.Remove(session);
            }

            session.Dispose();
            throw;
        }

        ActivePlaybackCountChanged?.Invoke(this, ActivePlaybackCount);
        return session;
    }

    public void PlayTestTone(string outputDeviceId)
    {
        _testOutput?.Stop();
        _testOutput?.Dispose();
        _testOutput = null;
        _testProvider = null;

        var outputDevice = devices.GetDevice(outputDeviceId);
        var signal = new SignalGenerator(44_100, 1)
        {
            Frequency = 660,
            Gain = 0.12,
            Type = SignalGeneratorType.Sin
        };
        _testProvider = signal.Take(TimeSpan.FromMilliseconds(650)).ToWaveProvider16();
        _testOutput = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 80);
        _testOutput.Init(_testProvider);
        _testOutput.PlaybackStopped += (_, _) =>
        {
            _testOutput?.Dispose();
            _testOutput = null;
            _testProvider = null;
        };
        _testOutput.Play();
    }

    public void StopAll()
    {
        PlaybackSession[] snapshot;
        lock (_sync)
        {
            snapshot = _sessions.ToArray();
        }

        foreach (var session in snapshot)
        {
            session.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAll();
        StopRouting();
        _testOutput?.Dispose();
    }

    private static BufferedWaveProvider CreateBuffer(WaveFormat waveFormat) => new(waveFormat)
    {
        DiscardOnBufferOverflow = true,
        ReadFully = true
    };

    internal static IWavePlayer CreateOutputWithRetry(Func<MMDevice> deviceFactory, IWaveProvider provider)
    {
        COMException? wasapiError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            WasapiOut? output = null;
            try
            {
                using var device = deviceFactory();
                output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 80);
                output.Init(provider);
                return output;
            }
            catch (COMException exception) when (
                exception.HResult == DeviceInvalidated || exception.HResult == EndpointCreateFailed)
            {
                output?.Dispose();
                wasapiError = exception;
                Thread.Sleep(250);
            }
            catch
            {
                output?.Dispose();
                throw;
            }
        }

        using var fallbackDevice = deviceFactory();
        var endpointGuidValue = fallbackDevice.Properties[PropertyKeys.PKEY_AudioEndpoint_GUID].Value?.ToString();
        if (!Guid.TryParse(endpointGuidValue, out var endpointGuid))
        {
            if (wasapiError is not null)
            {
                throw wasapiError;
            }

            throw new InvalidOperationException("The selected audio endpoint could not be opened.");
        }

        var fallback = new DirectSoundOut(endpointGuid, latency: 80);
        try
        {
            fallback.Init(provider);
            return fallback;
        }
        catch
        {
            fallback.Dispose();
            throw;
        }
    }
}

public sealed class PlaybackSession : IDisposable
{
    private readonly List<(IWavePlayer Output, AudioFileReader Reader)> _players = [];
    private readonly object _playersSync = new();
    private readonly Action _completed;
    private int _remaining;
    private int _completedRaised;
    private int _disposed;

    public PlaybackSession(
        string filePath,
        IEnumerable<string> outputDeviceIds,
        AudioDeviceService devices,
        float volume,
        Action completed)
    {
        _completed = completed;

        try
        {
            foreach (var outputDeviceId in outputDeviceIds)
            {
                var reader = new AudioFileReader(filePath);
                reader.Volume = volume;
                var output = AudioRouter.CreateOutputWithRetry(() => devices.GetDevice(outputDeviceId), reader);
                output.PlaybackStopped += (_, _) => PlayerStopped();
                _players.Add((output, reader));
            }

            _remaining = _players.Count;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Start()
    {
        foreach (var player in SnapshotPlayers())
        {
            player.Output.Play();
        }
    }

    public float Volume
    {
        set
        {
            lock (_playersSync)
            {
                foreach (var player in _players) player.Reader.Volume = value;
            }
        }
    }

    public void Stop()
    {
        foreach (var player in SnapshotPlayers())
        {
            if (player.Output.PlaybackState != PlaybackState.Stopped)
            {
                player.Output.Stop();
            }
        }
    }

    private void PlayerStopped()
    {
        if (Interlocked.Decrement(ref _remaining) <= 0 && Interlocked.Exchange(ref _completedRaised, 1) == 0)
        {
            Dispose();
            _completed();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        (IWavePlayer Output, AudioFileReader Reader)[] snapshot;
        lock (_playersSync)
        {
            snapshot = _players.ToArray();
            _players.Clear();
        }

        foreach (var player in snapshot)
        {
            player.Output.Dispose();
            player.Reader.Dispose();
        }
    }

    private (IWavePlayer Output, AudioFileReader Reader)[] SnapshotPlayers()
    {
        lock (_playersSync)
        {
            return _players.ToArray();
        }
    }
}
