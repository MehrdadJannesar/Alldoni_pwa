namespace Notesdoni.Services;

public interface IFileStorage
{
    Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken cancellationToken);
    Task UploadAsync(
        string fileName,
        string title,
        string category,
        string? description,
        bool hasAttachment,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);
    Task<StoredFileDownload?> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task UpdateMetadataAsync(
        string key,
        string title,
        string category,
        string? description,
        CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed record StoredFile(
    string Key,
    string FileName,
    string Title,
    string Category,
    string? Description,
    bool HasAttachment,
    long Size,
    DateTimeOffset? LastModified);

public sealed class StoredFileDownload(
    Stream content,
    string fileName,
    string contentType,
    IDisposable owner) : IDisposable
{
    public Stream Content { get; } = content;
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public void Dispose() => owner.Dispose();
}
