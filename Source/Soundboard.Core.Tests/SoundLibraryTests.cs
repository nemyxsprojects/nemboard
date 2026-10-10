using Soundboard.Core;

namespace Soundboard.Core.Tests;

public sealed class SoundLibraryTests
{
    [Theory]
    [InlineData("airhorn.mp3")]
    [InlineData("voice.WAV")]
    public void IsSupported_accepts_mp3_and_wav_case_insensitively(string path)
    {
        Assert.True(SoundLibrary.IsSupported(path));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("clip.ogg")]
    [InlineData("")]
    public void IsSupported_rejects_other_extensions(string path)
    {
        Assert.False(SoundLibrary.IsSupported(path));
    }

    [Fact]
    public void AddRange_deduplicates_the_same_canonical_path()
    {
        var library = new SoundLibrary();
        var clip = new SoundClip("1", "Été", @"C:\Sounds\summer.mp3", TimeSpan.FromSeconds(2), "F1");

        library.AddRange([clip, clip with { Id = "2", FilePath = @"c:\sounds\SUMMER.mp3" }]);

        Assert.Single(library.Items);
    }

    [Fact]
    public void Search_ignores_accents_and_case()
    {
        var library = new SoundLibrary();
        library.AddRange([
            new SoundClip("1", "Été brûlant", "summer.mp3", TimeSpan.Zero, "F1"),
            new SoundClip("2", "Airhorn", "airhorn.mp3", TimeSpan.Zero, "F2")
        ]);

        var result = library.Search("ETE");

        Assert.Collection(result, clip => Assert.Equal("Été brûlant", clip.Name));
    }
}
