namespace Soundboard.Core;

public sealed class AppSettings
{
    public string LanguageCode { get; set; } = "en";

    public string? MicrophoneDeviceId { get; set; }

    public string? CableDeviceId { get; set; }

    public string? MonitorDeviceId { get; set; }

    public bool SetupCompleted { get; set; }

    public bool OnboardingCompleted { get; set; }

    public double VoiceVolume { get; set; } = 1.0;

    public double SoundVolume { get; set; } = 1.0;

    public List<string> ImportedFiles { get; set; } = [];
}
