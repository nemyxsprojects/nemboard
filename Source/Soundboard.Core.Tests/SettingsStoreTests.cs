using Soundboard.Core;

namespace Soundboard.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task Save_then_load_round_trips_audio_and_library_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"soundboard-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        var expected = new AppSettings
        {
            LanguageCode = "fr",
            MicrophoneDeviceId = "mic-id",
            CableDeviceId = "cable-id",
            MonitorDeviceId = "headset-id",
            SetupCompleted = true,
            OnboardingCompleted = true,
            VoiceVolume = 1.35,
            SoundVolume = 0.75,
            ImportedFiles = [@"C:\Sounds\airhorn.mp3"]
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected.LanguageCode, actual.LanguageCode);
        Assert.Equal(expected.MicrophoneDeviceId, actual.MicrophoneDeviceId);
        Assert.Equal(expected.CableDeviceId, actual.CableDeviceId);
        Assert.Equal(expected.MonitorDeviceId, actual.MonitorDeviceId);
        Assert.True(actual.SetupCompleted);
        Assert.True(actual.OnboardingCompleted);
        Assert.Equal(expected.VoiceVolume, actual.VoiceVolume);
        Assert.Equal(expected.SoundVolume, actual.SoundVolume);
        Assert.Equal(expected.ImportedFiles, actual.ImportedFiles);
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task Load_returns_defaults_when_the_file_does_not_exist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"soundboard-missing-{Guid.NewGuid():N}", "settings.json");
        var store = new SettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.Equal("en", settings.LanguageCode);
        Assert.False(settings.SetupCompleted);
        Assert.False(settings.OnboardingCompleted);
        Assert.Equal(1.0, settings.VoiceVolume);
        Assert.Equal(1.0, settings.SoundVolume);
        Assert.Empty(settings.ImportedFiles);
    }
}
