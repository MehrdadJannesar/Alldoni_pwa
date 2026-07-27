namespace Musicdoni.Models;

public sealed record TrackUpload(
    string Title,
    string Artist,
    string Album,
    string Genre,
    string AudioFileName,
    string? AudioContentType,
    Stream AudioStream,
    long AudioSize,
    string? CoverFileName,
    string? CoverContentType,
    Stream? CoverStream);

