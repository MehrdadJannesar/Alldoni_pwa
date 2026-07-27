namespace Musicdoni.Models;

public sealed record MusicLibrary
{
    public List<Track> Tracks { get; init; } = [];
    public List<Playlist> Playlists { get; init; } = [];
}

