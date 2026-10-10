using System.Globalization;
using System.Text;

namespace Soundboard.Core;

public sealed class SoundLibrary
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav" };

    private readonly List<SoundClip> _items = [];

    public IReadOnlyList<SoundClip> Items => _items;

    public static bool IsSupported(string? path) =>
        !string.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    public void AddRange(IEnumerable<SoundClip> clips)
    {
        var paths = new HashSet<string>(
            _items.Select(item => Path.GetFullPath(item.FilePath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var clip in clips)
        {
            var canonicalPath = Path.GetFullPath(clip.FilePath);
            if (paths.Add(canonicalPath))
            {
                _items.Add(clip);
            }
        }
    }

    public IReadOnlyList<SoundClip> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return _items;
        }

        var normalizedQuery = Normalize(query);
        return _items
            .Where(item => Normalize(item.Name).Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
