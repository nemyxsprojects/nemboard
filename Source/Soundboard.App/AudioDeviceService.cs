using NAudio.CoreAudioApi;

namespace Soundboard.App;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDevice> GetMicrophones() => Enumerate(DataFlow.Capture);

    public IReadOnlyList<AudioDevice> GetOutputs() => Enumerate(DataFlow.Render);

    public AudioDevice? GetDefaultMicrophone() => GetDefault(DataFlow.Capture);

    public AudioDevice? GetDefaultOutput() => GetDefault(DataFlow.Render);

    public AudioDevice? FindCableInput()
    {
        var outputs = GetOutputs();
        return outputs.FirstOrDefault(device => device.Name.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase))
            ?? outputs.FirstOrDefault(device => device.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
    }

    public AudioDevice? FindCableOutput()
    {
        var microphones = GetMicrophones();
        return microphones.FirstOrDefault(device => device.Name.StartsWith("CABLE Output", StringComparison.OrdinalIgnoreCase))
            ?? microphones.FirstOrDefault(device => device.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));
    }

    public AudioDevice? FindBestMicrophone()
    {
        var defaultMicrophone = GetDefaultMicrophone();
        if (defaultMicrophone is not null && !IsStreamingDevice(defaultMicrophone) && !IsCable(defaultMicrophone))
        {
            return defaultMicrophone;
        }

        return GetMicrophones().FirstOrDefault(device => !IsStreamingDevice(device) && !IsCable(device));
    }

    public MMDevice GetDevice(string id)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }

    public bool IsActive(string? id, DataFlow flow) =>
        !string.IsNullOrWhiteSpace(id) && Enumerate(flow).Any(device => device.Id == id);

    public AudioDevice? FindBestMonitor()
    {
        var defaultOutput = GetDefaultOutput();
        if (defaultOutput is not null && !IsCable(defaultOutput))
        {
            return defaultOutput;
        }

        return GetOutputs().FirstOrDefault(device => !IsCable(device) && !IsStreamingDevice(device));
    }

    public static bool IsStreamingDevice(AudioDevice device) =>
        device.Name.Contains("Steam Streaming", StringComparison.OrdinalIgnoreCase);

    private static bool IsCable(AudioDevice device) =>
        device.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
        device.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

    private static AudioDevice? GetDefault(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return new AudioDevice(device.ID, device.FriendlyName);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AudioDevice> Enumerate(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(device => new AudioDevice(device.ID, device.FriendlyName))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
