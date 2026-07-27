namespace Musicdoni.Models;

public sealed record Track
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public required string Genre { get; init; }
    public required string AudioObjectKey { get; init; }
    public string? CoverObjectKey { get; init; }
    public required string ContentType { get; init; }
    public long Size { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

