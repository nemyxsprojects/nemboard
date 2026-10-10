using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Soundboard.App;

/// <summary>
/// Captures only the audio rendered by one Windows process tree. This keeps call audio and
/// every other desktop application out of the virtual microphone mix.
/// </summary>
public sealed class ProcessLoopbackCapture : IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";
    private const ushort VtBlob = 65;
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private readonly AudioClient _audioClient;
    private readonly EventWaitHandle _frameEvent = new(false, EventResetMode.AutoReset);
    private readonly byte[] _recordBuffer;
    private readonly int _bytesPerFrame;
    private Thread? _captureThread;
    private volatile bool _recording;
    private bool _disposed;

    private ProcessLoopbackCapture(AudioClient audioClient)
    {
        _audioClient = audioClient;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        _audioClient.Initialize(
            AudioClientShareMode.Shared,
            AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback,
            800_000,
            0,
            WaveFormat,
            Guid.Empty);
        _audioClient.SetEventHandle(_frameEvent.SafeWaitHandle.DangerousGetHandle());
        _bytesPerFrame = WaveFormat.BlockAlign;
        _recordBuffer = new byte[_audioClient.BufferSize * _bytesPerFrame];
    }

    public WaveFormat WaveFormat { get; }

    public event EventHandler<WaveInEventArgs>? DataAvailable;

    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static async Task<ProcessLoopbackCapture> CreateAsync(uint processId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("Isolated browser audio requires Windows 10 version 2004 or later.");
        }

        var activation = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = processId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree
            }
        };

        var activationPointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
        var variantPointer = Marshal.AllocHGlobal(Marshal.SizeOf<BlobPropVariant>());
        try
        {
            Marshal.StructureToPtr(activation, activationPointer, false);
            Marshal.StructureToPtr(new BlobPropVariant
            {
                VariantType = VtBlob,
                BlobSize = (uint)Marshal.SizeOf<AudioClientActivationParams>(),
                BlobData = activationPointer
            }, variantPointer, false);

            var completion = new ActivationCompletionHandler();
            ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                IidAudioClient,
                variantPointer,
                completion,
                out var operation);
            var audioClientInterface = await completion.Task.ConfigureAwait(false);
            GC.KeepAlive(operation);
            return new ProcessLoopbackCapture(new AudioClient(audioClientInterface));
        }
        finally
        {
            Marshal.FreeHGlobal(variantPointer);
            Marshal.FreeHGlobal(activationPointer);
        }
    }

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_recording)
        {
            return;
        }

        _recording = true;
        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "NemBoard WebView2 audio capture"
        };
        _captureThread.Start();
    }

    public void StopRecording()
    {
        _recording = false;
        _frameEvent.Set();
        if (_captureThread is not null && _captureThread != Thread.CurrentThread)
        {
            _captureThread.Join(TimeSpan.FromSeconds(2));
            _captureThread = null;
        }
    }

    private void CaptureLoop()
    {
        Exception? error = null;
        try
        {
            var capture = _audioClient.AudioCaptureClient;
            _audioClient.Start();
            while (_recording)
            {
                _frameEvent.WaitOne(250);
                if (!_recording)
                {
                    break;
                }

                ReadAvailablePackets(capture);
            }
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            try { _audioClient.Stop(); } catch { }
            _recording = false;
            RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
        }
    }

    private void ReadAvailablePackets(AudioCaptureClient capture)
    {
        var offset = 0;
        var packetFrames = capture.GetNextPacketSize();
        while (packetFrames > 0)
        {
            var pointer = capture.GetBuffer(out var frames, out var flags);
            var bytes = frames * _bytesPerFrame;
            if (offset + bytes > _recordBuffer.Length && offset > 0)
            {
                DataAvailable?.Invoke(this, new WaveInEventArgs(_recordBuffer, offset));
                offset = 0;
            }

            if ((flags & AudioClientBufferFlags.Silent) != 0)
            {
                Array.Clear(_recordBuffer, offset, bytes);
            }
            else
            {
                Marshal.Copy(pointer, _recordBuffer, offset, bytes);
            }

            offset += bytes;
            capture.ReleaseBuffer(frames);
            packetFrames = capture.GetNextPacketSize();
        }

        if (offset > 0)
        {
            DataAvailable?.Invoke(this, new WaveInEventArgs(_recordBuffer, offset));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopRecording();
        _frameEvent.Dispose();
        _audioClient.Dispose();
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    private enum AudioClientActivationType
    {
        Default,
        ProcessLoopback
    }

    private enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree,
        ExcludeTargetProcessTree
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct BlobPropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public uint BlobSize;
        [FieldOffset(16)] public IntPtr BlobData;
    }

    [ComVisible(true)]
    private sealed class ActivationCompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        private readonly TaskCompletionSource<IAudioClient> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IAudioClient> Task => _completion.Task;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out var result, out var activatedInterface);
                if (result != 0)
                {
                    _completion.TrySetException(Marshal.GetExceptionForHR(result) ?? new COMException("Audio activation failed.", result));
                    return;
                }

                _completion.TrySetResult((IAudioClient)activatedInterface);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }
    }

    [ComImport]
    [Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }
}
