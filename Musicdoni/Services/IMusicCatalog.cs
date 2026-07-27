using Musicdoni.Models;

namespace Musicdoni.Services;

public interface IMusicCatalog
{
    Task<MusicLibrary> GetLibraryAsync(CancellationToken cancellationToken);
    Task<Track?> GetTrackAsync(Guid id, CancellationToken cancellationToken);
    Task<Track> AddTrackAsync(TrackUpload upload, CancellationToken cancellationToken);
    Task<bool> DeleteTrackAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> DeleteTracksAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<string> CreatePlaybackUrlAsync(Track track, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<string> CreateCoverUrlAsync(Track track, TimeSpan lifetime, CancellationToken cancellationToken);
    Task<Playlist> CreatePlaylistAsync(string name, CancellationToken cancellationToken);
    Task<Playlist?> AddTrackToPlaylistAsync(Guid playlistId, Guid trackId, CancellationToken cancellationToken);
    Task<Playlist?> RemoveTrackFromPlaylistAsync(Guid playlistId, Guid trackId, CancellationToken cancellationToken);
}

