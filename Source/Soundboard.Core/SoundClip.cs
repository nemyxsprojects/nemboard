namespace Soundboard.Core;

public sealed record SoundClip(
    string Id,
    string Name,
    string FilePath,
    TimeSpan Duration,
    string Hotkey);
