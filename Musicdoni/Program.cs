using Musicdoni.Models;
using Musicdoni.Services;
using Alldoni.Shared.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;

const long MaxUploadBytes = 4L * 1024 * 1024 * 1024;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = MaxUploadBytes;
});
builder.Services.Configure<ArvanStorageOptions>(
    builder.Configuration.GetSection(ArvanStorageOptions.SectionName));
builder.Services.AddSingleton<IArvanStorageClient, ArvanStorageClient>();
builder.Services.AddSingleton<IMusicCatalog, S3MusicCatalog>();
builder.Services.AddMemoryCache();
builder.Services.AddAlldoniSecurity(builder.Environment);

var app = builder.Build();

app.UseRouting();
app.UseAlldoniSecurity();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (IOptions<ArvanStorageOptions> storageOptions) =>
{
    var options = storageOptions.Value;
    return Results.Ok(new
    {
        app = "Musicdoni",
        storage = new
        {
            options.Endpoint,
            options.Region,
            bucketConfigured = !string.IsNullOrWhiteSpace(options.BucketName),
            keysConfigured = !string.IsNullOrWhiteSpace(options.AccessKey)
                && !string.IsNullOrWhiteSpace(options.SecretKey)
        }
    });
});


app.MapGet("/api/storage/usage", async (
    IArvanStorageClient storage,
    IOptions<ArvanStorageOptions> storageOptions,
    CancellationToken cancellationToken) =>
{
    var options = storageOptions.Value;
    if (!IsStorageConfigured(options))
    {
        return Results.Ok(new StorageUsageResponse(false, 0, 0, options.StorageLimitBytes, null));
    }

    try
    {
        var objects = await storage.ListObjectsAsync(null, cancellationToken);
        var usedBytes = objects.Sum(item => item.Size);
        var limitBytes = options.StorageLimitBytes is > 0 ? options.StorageLimitBytes : null;
        long? availableBytes = limitBytes is null ? null : Math.Max(limitBytes.Value - usedBytes, 0);
        return Results.Ok(new StorageUsageResponse(true, usedBytes, objects.Count, limitBytes, availableBytes));
    }
    catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
    {
        return Results.Json(new ProblemDetailsDto(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapGet("/api/tracks", async (IMusicCatalog catalog, CancellationToken cancellationToken) =>
{
    var library = await catalog.GetLibraryAsync(cancellationToken);
    return Results.Ok(library.Tracks.OrderByDescending(track => track.CreatedAt));
});

app.MapPost("/api/tracks", async (
    HttpRequest request,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new ProblemDetailsDto("Upload must use multipart/form-data."));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var audio = form.Files.GetFile("audio");
    if (audio is null || audio.Length == 0)
    {
        return Results.BadRequest(new ProblemDetailsDto("Choose an audio file to upload."));
    }

    var title = ReadField(form, "title", Path.GetFileNameWithoutExtension(audio.FileName));
    var artist = ReadField(form, "artist", "Unknown Artist");
    var album = ReadField(form, "album", "Singles");
    var genre = ReadField(form, "genre", "Music");
    var cover = form.Files.GetFile("cover");

    await using var audioStream = audio.OpenReadStream();
    await using var coverStream = cover?.OpenReadStream();
    Track track;
    try
    {
        track = await catalog.AddTrackAsync(
            new TrackUpload(
                title,
                artist,
                album,
                genre,
                audio.FileName,
                audio.ContentType,
                audioStream,
                audio.Length,
                cover?.FileName,
                cover?.ContentType,
                coverStream),
            cancellationToken);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Json(new ProblemDetailsDto(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Created($"/api/tracks/{track.Id}", track);
});


app.MapPost("/api/tracks/bulk", async (
    HttpRequest request,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new ProblemDetailsDto("Upload must use multipart/form-data."));
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var audioFiles = form.Files.GetFiles("audio").Where(file => file.Length > 0).ToList();
    if (audioFiles.Count == 0)
    {
        return Results.BadRequest(new ProblemDetailsDto("Choose one or more audio files to upload."));
    }

    var artist = ReadField(form, "artist", "Unknown Artist");
    var album = ReadField(form, "album", "Singles");
    var genre = ReadField(form, "genre", "Music");
    var tracks = new List<Track>();

    try
    {
        foreach (var audio in audioFiles)
        {
            await using var audioStream = audio.OpenReadStream();
            var title = Path.GetFileNameWithoutExtension(audio.FileName);
            tracks.Add(await catalog.AddTrackAsync(
                new TrackUpload(
                    string.IsNullOrWhiteSpace(title) ? "Untitled track" : title,
                    artist,
                    album,
                    genre,
                    audio.FileName,
                    audio.ContentType,
                    audioStream,
                    audio.Length,
                    null,
                    null,
                    null),
                cancellationToken));
        }
    }
    catch (InvalidOperationException exception)
    {
        return Results.Json(new ProblemDetailsDto(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Created("/api/tracks", tracks);
});
app.MapGet("/api/tracks/{id:guid}", async (
    Guid id,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var track = await catalog.GetTrackAsync(id, cancellationToken);
    return track is null ? Results.NotFound() : Results.Ok(track);
});

app.MapPost("/api/tracks/{id:guid}/stream-token", async (
    Guid id,
    IMusicCatalog catalog,
    IMemoryCache cache,
    CancellationToken cancellationToken) =>
{
    var track = await catalog.GetTrackAsync(id, cancellationToken);
    if (track is null)
    {
        return Results.NotFound();
    }

    var token = CreatePlaybackToken();
    cache.Set(GetPlaybackTokenCacheKey(token), id, TimeSpan.FromSeconds(60));
    return Results.Ok(new StreamTokenResponse(token, 60));
});

app.MapGet("/api/tracks/{id:guid}/stream", async (
    Guid id,
    string? token,
    HttpRequest request,
    HttpResponse response,
    IMusicCatalog catalog,
    IArvanStorageClient storage,
    IMemoryCache cache,
    CancellationToken cancellationToken) =>
{
    if (IsDownloadManagerRequest(request))
    {
        return Results.Json(
            new ProblemDetailsDto("Downloads are disabled. Please use the Musicdoni player."),
            statusCode: StatusCodes.Status403Forbidden);
    }

    if (string.IsNullOrWhiteSpace(token)
        || !cache.TryGetValue<Guid>(GetPlaybackTokenCacheKey(token), out var tokenTrackId)
        || tokenTrackId != id)
    {
        return Results.Json(
            new ProblemDetailsDto("This playback link has expired. Please press play again."),
            statusCode: StatusCodes.Status403Forbidden);
    }
    var track = await catalog.GetTrackAsync(id, cancellationToken);
    if (track is null)
    {
        return Results.NotFound();
    }

    var stream = await storage.OpenReadObjectAsync(track.AudioObjectKey, request.Headers.Range.ToString(), cancellationToken);
    response.RegisterForDispose(stream);
    response.StatusCode = stream.StatusCode;
    response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
    response.Headers.Pragma = "no-cache";
    response.Headers.Expires = "0";
    response.Headers.XContentTypeOptions = "nosniff";
    response.Headers.ContentDisposition = $"inline; filename=\"{CreateSafeFileName(track)}\"";
    response.Headers.AcceptRanges = "bytes";

    var contentRange = stream.ContentRange;
    if (string.IsNullOrWhiteSpace(contentRange)
        && TryCreateContentRange(request.Headers.Range.ToString(), track.Size, out var computedContentRange))
    {
        contentRange = computedContentRange;
    }

    if (!string.IsNullOrWhiteSpace(contentRange))
    {
        response.Headers.ContentRange = contentRange;
    }

    if (stream.ContentLength is > 0)
    {
        response.ContentLength = stream.ContentLength.Value;
    }

    return Results.Stream(
        stream.Content,
        stream.ContentType ?? track.ContentType,
        enableRangeProcessing: false);
});

app.MapGet("/api/tracks/{id:guid}/cover", async (
    Guid id,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var track = await catalog.GetTrackAsync(id, cancellationToken);
    if (track?.CoverObjectKey is null)
    {
        return Results.NotFound();
    }

    var url = await catalog.CreateCoverUrlAsync(track, TimeSpan.FromHours(6), cancellationToken);
    return Results.Redirect(url);
});

app.MapDelete("/api/tracks/{id:guid}", async (
    Guid id,
    [FromBody] DeleteTrackRequest request,
    IMusicCatalog catalog,
    PasswordStore passwordStore,
    CancellationToken cancellationToken) =>
{
    if (!passwordStore.Verify(request.Password))
    {
        return Results.Json(new ProblemDetailsDto("Admin password is required."), statusCode: StatusCodes.Status401Unauthorized);
    }

    var deleted = await catalog.DeleteTrackAsync(id, cancellationToken);
    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapDelete("/api/tracks/delete", async (
    [FromBody] BulkDeleteTracksRequest request,
    IMusicCatalog catalog,
    PasswordStore passwordStore,
    CancellationToken cancellationToken) =>
{
    if (!passwordStore.Verify(request.Password))
    {
        return Results.Json(new ProblemDetailsDto("Admin password is required."), statusCode: StatusCodes.Status401Unauthorized);
    }

    if (request.TrackIds is null || request.TrackIds.Length == 0)
    {
        return Results.BadRequest(new ProblemDetailsDto("Choose at least one track to delete."));
    }

    var deletedIds = await catalog.DeleteTracksAsync(request.TrackIds, cancellationToken);
    return Results.Ok(new BulkDeleteTracksResponse(deletedIds));
});

app.MapGet("/api/playlists", async (IMusicCatalog catalog, CancellationToken cancellationToken) =>
{
    var library = await catalog.GetLibraryAsync(cancellationToken);
    return Results.Ok(library.Playlists.OrderBy(playlist => playlist.Name));
});

app.MapPost("/api/playlists", async (
    PlaylistRequest request,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new ProblemDetailsDto("Playlist name is required."));
    }

    Playlist playlist;
    try
    {
        playlist = await catalog.CreatePlaylistAsync(request.Name, cancellationToken);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Json(new ProblemDetailsDto(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Created($"/api/playlists/{playlist.Id}", playlist);
});

app.MapPost("/api/playlists/{playlistId:guid}/tracks/{trackId:guid}", async (
    Guid playlistId,
    Guid trackId,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var updated = await catalog.AddTrackToPlaylistAsync(playlistId, trackId, cancellationToken);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});


app.MapDelete("/api/playlists/{playlistId:guid}/tracks/{trackId:guid}", async (
    Guid playlistId,
    Guid trackId,
    IMusicCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var updated = await catalog.RemoveTrackFromPlaylistAsync(playlistId, trackId, cancellationToken);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
app.MapFallbackToFile("index.html");

app.Run();

static bool IsStorageConfigured(ArvanStorageOptions options)
{
    return !string.IsNullOrWhiteSpace(options.Endpoint)
        && !string.IsNullOrWhiteSpace(options.Region)
        && !string.IsNullOrWhiteSpace(options.BucketName)
        && !string.IsNullOrWhiteSpace(options.AccessKey)
        && !string.IsNullOrWhiteSpace(options.SecretKey);
}
static string ReadField(IFormCollection form, string name, string fallback)
{
    var value = form[name].ToString();
    return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}



static string CreatePlaybackToken()
{
    return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
}

static string GetPlaybackTokenCacheKey(string token)
{
    return $"playback-token:{token}";
}

static bool IsDownloadManagerRequest(HttpRequest request)
{
    var userAgent = request.Headers.UserAgent.ToString();
    if (string.IsNullOrWhiteSpace(userAgent))
    {
        return false;
    }

    var blockedAgents = new[]
    {
        "internet download manager",
        " idm",
        "idman",
        "free download manager",
        "fdm",
        "jdownloader",
        "download accelerator",
        "eagleget",
        "flashget",
        "gopeed",
        "aria2",
        "uget"
    };

    return blockedAgents.Any(agent => userAgent.Contains(agent, StringComparison.OrdinalIgnoreCase));
}

static bool TryCreateContentRange(string? rangeHeader, long totalSize, out string contentRange)
{
    contentRange = string.Empty;
    if (totalSize <= 0
        || string.IsNullOrWhiteSpace(rangeHeader)
        || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var range = rangeHeader["bytes=".Length..].Split(',', 2)[0].Trim();
    var parts = range.Split('-', 2);
    if (parts.Length != 2)
    {
        return false;
    }

    long start;
    long end;
    if (string.IsNullOrWhiteSpace(parts[0]))
    {
        if (!long.TryParse(parts[1], out var suffixLength) || suffixLength <= 0)
        {
            return false;
        }

        start = Math.Max(totalSize - suffixLength, 0);
        end = totalSize - 1;
    }
    else
    {
        if (!long.TryParse(parts[0], out start) || start < 0 || start >= totalSize)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            end = totalSize - 1;
        }
        else if (!long.TryParse(parts[1], out var requestedEnd))
        {
            return false;
        }
        else
        {
            end = Math.Min(requestedEnd, totalSize - 1);
        }
    }

    if (end < start)
    {
        return false;
    }

    contentRange = $"bytes {start}-{end}/{totalSize}";
    return true;
}
static string CreateSafeFileName(Track track)
{
    var extension = Path.GetExtension(track.AudioObjectKey);
    if (string.IsNullOrWhiteSpace(extension))
    {
        extension = ".mp3";
    }

    var invalidCharacters = Path.GetInvalidFileNameChars();
    var safeTitle = new string(track.Title
        .Select(character => invalidCharacters.Contains(character) ? '_' : character)
        .ToArray())
        .Trim();

    return string.IsNullOrWhiteSpace(safeTitle)
        ? $"track{extension}"
        : $"{safeTitle}{extension}";
}
public sealed record StorageUsageResponse(bool Configured, long UsedBytes, int ObjectCount, long? LimitBytes, long? AvailableBytes);
public sealed record StreamTokenResponse(string Token, int ExpiresInSeconds);
public sealed record DeleteTrackRequest(string Password);
public sealed record BulkDeleteTracksRequest(Guid[] TrackIds, string Password);
public sealed record BulkDeleteTracksResponse(IReadOnlyList<Guid> DeletedIds);
public sealed record PlaylistRequest(string Name);
public sealed record ProblemDetailsDto(string Message);



















