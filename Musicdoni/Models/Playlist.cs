namespace Musicdoni.Models;

public sealed record Playlist
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public List<Guid> TrackIds { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

