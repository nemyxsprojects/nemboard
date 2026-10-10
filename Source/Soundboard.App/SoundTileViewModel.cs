using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Soundboard.Core;

namespace Soundboard.App;

public sealed class SoundTileViewModel(SoundClip clip) : INotifyPropertyChanged
{
    private bool _isPlaying;

    public SoundClip Clip { get; } = clip;

    public string Name => Clip.Name;

    public string FilePath => Clip.FilePath;

    public string Duration => Clip.Duration.TotalMinutes >= 1
        ? Clip.Duration.ToString(@"m\:ss")
        : $"0:{Clip.Duration.Seconds:00}";

    public string Hotkey => Clip.Hotkey;

    public Brush ArtworkBrush
    {
        get
        {
            var tones = new[] { "#EFEFED", "#A9A9A4", "#343434", "#111111" };
            var index = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(Name)) % tones.Length;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(tones[index]));
        }
    }

    public Brush ArtworkForeground =>
        ((SolidColorBrush)ArtworkBrush).Color.R > 120 ? Brushes.Black : Brushes.White;

    public string Initials
    {
        get
        {
            var words = Name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
