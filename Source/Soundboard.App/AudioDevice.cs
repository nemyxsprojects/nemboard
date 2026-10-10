namespace Soundboard.App;

public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
