namespace Musicdoni.Services;

public interface IArvanStorageClient
{
    Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);
    Task<string?> GetTextObjectAsync(string objectKey, CancellationToken cancellationToken);
    Task PutTextObjectAsync(string objectKey, string content, CancellationToken cancellationToken);
    Task DeleteObjectAsync(string objectKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArvanObjectInfo>> ListObjectsAsync(string? prefix, CancellationToken cancellationToken);
    Task<ArvanObjectReadResult> OpenReadObjectAsync(string objectKey, string? rangeHeader, CancellationToken cancellationToken);
    string CreatePresignedGetUrl(string objectKey, TimeSpan lifetime);
}

public sealed record ArvanObjectInfo(string Key, long Size, DateTimeOffset? LastModified);

public sealed class ArvanObjectReadResult(
    Stream content,
    string? contentType,
    long? contentLength,
    string? contentRange,
    int statusCode,
    IDisposable owner) : IDisposable
{
    public Stream Content { get; } = content;
    public string? ContentType { get; } = contentType;
    public long? ContentLength { get; } = contentLength;
    public string? ContentRange { get; } = contentRange;
    public int StatusCode { get; } = statusCode;

    public void Dispose()
    {
        owner.Dispose();
    }
}
