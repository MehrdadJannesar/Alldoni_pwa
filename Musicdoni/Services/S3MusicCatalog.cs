using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Musicdoni.Models;

namespace Musicdoni.Services;

public sealed class S3MusicCatalog(
    IArvanStorageClient storage,
    IOptions<ArvanStorageOptions> options) : IMusicCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".webm"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ArvanStorageOptions _options = options.Value;

    public async Task<MusicLibrary> GetLibraryAsync(CancellationToken cancellationToken)
    {
        if (!IsStorageConfigured())
        {
            return SeedLibrary();
        }

        var json = await storage.GetTextObjectAsync(_options.CatalogKey, cancellationToken);
        var library = string.IsNullOrWhiteSpace(json)
            ? SeedLibrary()
            : JsonSerializer.Deserialize<MusicLibrary>(json, JsonOptions) ?? SeedLibrary();

        if (await ImportExistingAudioObjectsAsync(library, cancellationToken))
        {
            await SaveLibraryAsync(library, cancellationToken);
        }

        return library;
    }

    public async Task<Track?> GetTrackAsync(Guid id, CancellationToken cancellationToken)
    {
        var library = await GetLibraryAsync(cancellationToken);
        return library.Tracks.FirstOrDefault(track => track.Id == id);
    }

    public async Task<Track> AddTrackAsync(TrackUpload upload, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var trackId = Guid.NewGuid();
            var audioKey = $"{_options.TracksPrefix}/{trackId}{GetSafeExtension(upload.AudioFileName, ".mp3")}";
            var contentType = string.IsNullOrWhiteSpace(upload.AudioContentType)
                ? "audio/mpeg"
                : upload.AudioContentType;

            await storage.PutObjectAsync(audioKey, upload.AudioStream, contentType, cancellationToken);

            string? coverKey = null;
            if (upload.CoverStream is not null)
            {
                coverKey = $"{_options.CoversPrefix}/{trackId}{GetSafeExtension(upload.CoverFileName, ".jpg")}";
                await storage.PutObjectAsync(
                    coverKey,
                    upload.CoverStream,
                    string.IsNullOrWhiteSpace(upload.CoverContentType) ? "image/jpeg" : upload.CoverContentType,
                    cancellationToken);
            }

            var library = await GetLibraryAsync(cancellationToken);
            var track = new Track
            {
                Id = trackId,
                Title = upload.Title,
                Artist = upload.Artist,
                Album = upload.Album,
                Genre = upload.Genre,
                AudioObjectKey = audioKey,
                CoverObjectKey = coverKey,
                ContentType = contentType,
                Size = upload.AudioSize
            };

            library.Tracks.RemoveAll(item => item.AudioObjectKey == audioKey);
            library.Tracks.Add(track);
            await SaveLibraryAsync(library, cancellationToken);
            return track;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteTrackAsync(Guid id, CancellationToken cancellationToken)
    {
        var deletedIds = await DeleteTracksAsync([id], cancellationToken);
        return deletedIds.Count > 0;
    }

    public async Task<IReadOnlyList<Guid>> DeleteTracksAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var requestedIds = ids.ToHashSet();
            var library = await GetLibraryAsync(cancellationToken);
            var tracks = library.Tracks
                .Where(track => requestedIds.Contains(track.Id))
                .ToList();

            if (tracks.Count == 0)
            {
                return [];
            }

            var deletedIds = tracks.Select(track => track.Id).ToHashSet();
            library.Tracks.RemoveAll(track => deletedIds.Contains(track.Id));
            foreach (var playlist in library.Playlists)
            {
                playlist.TrackIds.RemoveAll(trackId => deletedIds.Contains(trackId));
            }

            await SaveLibraryAsync(library, cancellationToken);

            foreach (var track in tracks)
            {
                await storage.DeleteObjectAsync(track.AudioObjectKey, cancellationToken);
                if (track.CoverObjectKey is not null)
                {
                    await storage.DeleteObjectAsync(track.CoverObjectKey, cancellationToken);
                }
            }

            return tracks.Select(track => track.Id).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<string> CreatePlaybackUrlAsync(Track track, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        return Task.FromResult(storage.CreatePresignedGetUrl(track.AudioObjectKey, lifetime));
    }

    public Task<string> CreateCoverUrlAsync(Track track, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (track.CoverObjectKey is null)
        {
            throw new InvalidOperationException("Track does not have cover art.");
        }

        return Task.FromResult(storage.CreatePresignedGetUrl(track.CoverObjectKey, lifetime));
    }

    public async Task<Playlist> CreatePlaylistAsync(string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var library = await GetLibraryAsync(cancellationToken);
            var playlist = new Playlist { Name = name.Trim() };
            library.Playlists.Add(playlist);
            await SaveLibraryAsync(library, cancellationToken);
            return playlist;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Playlist?> AddTrackToPlaylistAsync(
        Guid playlistId,
        Guid trackId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var library = await GetLibraryAsync(cancellationToken);
            var playlist = library.Playlists.FirstOrDefault(item => item.Id == playlistId);
            if (playlist is null || library.Tracks.All(track => track.Id != trackId))
            {
                return null;
            }

            if (!playlist.TrackIds.Contains(trackId))
            {
                playlist.TrackIds.Add(trackId);
                await SaveLibraryAsync(library, cancellationToken);
            }

            return playlist;
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task<Playlist?> RemoveTrackFromPlaylistAsync(
        Guid playlistId,
        Guid trackId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var library = await GetLibraryAsync(cancellationToken);
            var playlist = library.Playlists.FirstOrDefault(item => item.Id == playlistId);
            if (playlist is null)
            {
                return null;
            }

            if (playlist.TrackIds.Remove(trackId))
            {
                await SaveLibraryAsync(library, cancellationToken);
            }

            return playlist;
        }
        finally
        {
            _gate.Release();
        }
    }
    private async Task<bool> ImportExistingAudioObjectsAsync(
        MusicLibrary library,
        CancellationToken cancellationToken)
    {
        var objects = await storage.ListObjectsAsync(null, cancellationToken);
        var knownKeys = library.Tracks
            .Select(track => track.AudioObjectKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = false;

        foreach (var item in objects.Where(IsImportableAudioObject))
        {
            if (!knownKeys.Add(item.Key))
            {
                continue;
            }

            library.Tracks.Add(new Track
            {
                Id = CreateDeterministicGuid(item.Key),
                Title = GetTitleFromKey(item.Key),
                Artist = "Arvan Cloud",
                Album = "Uploaded Songs",
                Genre = "Music",
                AudioObjectKey = item.Key,
                ContentType = GetContentType(item.Key),
                Size = item.Size,
                CreatedAt = item.LastModified ?? DateTimeOffset.UtcNow
            });
            imported = true;
        }

        return imported;
    }

    private bool IsImportableAudioObject(ArvanObjectInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Key)
            || item.Key.EndsWith("/", StringComparison.Ordinal)
            || item.Key.Equals(_options.CatalogKey, StringComparison.OrdinalIgnoreCase)
            || item.Key.StartsWith(_options.CoversPrefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AudioExtensions.Contains(Path.GetExtension(item.Key));
    }

    private Task SaveLibraryAsync(MusicLibrary library, CancellationToken cancellationToken)
    {
        EnsureStorageConfigured();

        var json = JsonSerializer.Serialize(library, JsonOptions);
        return storage.PutTextObjectAsync(_options.CatalogKey, json, cancellationToken);
    }

    private bool IsStorageConfigured()
    {
        return !string.IsNullOrWhiteSpace(_options.Endpoint)
            && !string.IsNullOrWhiteSpace(_options.Region)
            && !string.IsNullOrWhiteSpace(_options.BucketName)
            && !string.IsNullOrWhiteSpace(_options.AccessKey)
            && !string.IsNullOrWhiteSpace(_options.SecretKey);
    }

    private void EnsureStorageConfigured()
    {
        if (!IsStorageConfigured())
        {
            throw new InvalidOperationException(
                "Arvan storage is not configured. Set ArvanStorage:BucketName, AccessKey, and SecretKey before uploading or changing playlists.");
        }
    }

    private static MusicLibrary SeedLibrary()
    {
        return new MusicLibrary
        {
            Playlists =
            [
                new Playlist { Name = "Liked Songs" },
                new Playlist { Name = "Fresh Uploads" }
            ]
        };
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }

    private static string GetTitleFromKey(string key)
    {
        var title = Path.GetFileNameWithoutExtension(key.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(title) ? key : title;
    }

    private static string GetContentType(string key)
    {
        return Path.GetExtension(key).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/ogg",
            ".flac" => "audio/flac",
            ".webm" => "audio/webm",
            _ => "audio/mpeg"
        };
    }

    private static string GetSafeExtension(string? fileName, string fallback)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 12)
        {
            return fallback;
        }

        return extension.ToLowerInvariant();
    }
}


