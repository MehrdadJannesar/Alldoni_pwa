using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Filedoni.Services;

public sealed class ArvanFileStorage(
    IOptions<ArvanStorageOptions> options,
    ILogger<ArvanFileStorage> logger) : IFileStorage
{
    private readonly ArvanStorageOptions _options = options.Value;

    public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        try
        {
            using var client = CreateClient();
            var files = new List<StoredFile>();
            string? token = null;
            do
            {
                var response = await client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    Prefix = Prefix,
                    ContinuationToken = token
                }, cancellationToken);

                var pageFiles = await Task.WhenAll((response.S3Objects ?? [])
                    .Where(item => !item.Key.EndsWith('/'))
                    .Select(item => CreateStoredFileAsync(client, item, cancellationToken)));
                files.AddRange(pageFiles);
                token = response.NextContinuationToken;
            }
            while (!string.IsNullOrEmpty(token));

            return files.OrderByDescending(file => file.LastModified).ToList();
        }
        catch (AmazonS3Exception exception)
        {
            throw CreateStorageException(exception, Prefix);
        }
    }

    public async Task UploadAsync(
        string fileName,
        string title,
        string category,
        string? description,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var safeName = Path.GetFileName(fileName);
        var key = $"{Prefix}/{Guid.NewGuid():N}/{Uri.EscapeDataString(safeName)}";
        try
        {
            using var client = CreateClient();
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = content,
                ContentType = string.IsNullOrWhiteSpace(contentType)
                    ? "application/octet-stream"
                    : contentType
            };
            request.Metadata["title"] = EncodeMetadata(title.Trim());
            request.Metadata["category"] = EncodeMetadata(category.Trim());
            request.Metadata["description"] = EncodeMetadata(description?.Trim() ?? "");
            await client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            throw CreateStorageException(exception, key);
        }
    }

    public async Task<StoredFileDownload?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        EnsureValidKey(key);
        try
        {
            var client = CreateClient();
            var response = await client.GetObjectAsync(_options.BucketName, key, cancellationToken);
            return new StoredFileDownload(
                response.ResponseStream,
                GetFileName(key),
                response.Headers.ContentType ?? "application/octet-stream",
                new CompositeDisposable(response, client));
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            throw CreateStorageException(exception, key);
        }
    }

    public async Task UpdateMetadataAsync(
        string key,
        string title,
        string category,
        string? description,
        CancellationToken cancellationToken)
    {
        EnsureValidKey(key);
        try
        {
            using var client = CreateClient();
            var current = await client.GetObjectMetadataAsync(
                _options.BucketName,
                key,
                cancellationToken);
            var request = new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = key,
                DestinationBucket = _options.BucketName,
                DestinationKey = key,
                MetadataDirective = S3MetadataDirective.REPLACE,
                ContentType = current.Headers.ContentType
            };
            request.Metadata["title"] = EncodeMetadata(title.Trim());
            request.Metadata["category"] = EncodeMetadata(category.Trim());
            request.Metadata["description"] = EncodeMetadata(description?.Trim() ?? "");
            await client.CopyObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            throw CreateStorageException(exception, key);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        EnsureValidKey(key);
        try
        {
            using var client = CreateClient();
            await client.DeleteObjectAsync(_options.BucketName, key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (AmazonS3Exception exception)
        {
            throw CreateStorageException(exception, key);
        }
    }

    private string Prefix => _options.FilesPrefix.Trim().Trim('/');

    private async Task<StoredFile> CreateStoredFileAsync(
        AmazonS3Client client,
        S3Object item,
        CancellationToken cancellationToken)
    {
        var fileName = GetFileName(item.Key);
        var metadata = await client.GetObjectMetadataAsync(
            _options.BucketName,
            item.Key,
            cancellationToken);

        return new StoredFile(
            item.Key,
            fileName,
            DecodeMetadata(metadata.Metadata["title"], fileName),
            DecodeMetadata(metadata.Metadata["category"], "Uncategorized"),
            DecodeMetadata(metadata.Metadata["description"], ""),
            item.Size.GetValueOrDefault(),
            item.LastModified);
    }

    private AmazonS3Client CreateClient()
    {
        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        return new AmazonS3Client(credentials, new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            AuthenticationRegion = _options.Region,
            ForcePathStyle = true
        });
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Arvan storage is not configured. Set Endpoint, BucketName, AccessKey, and SecretKey.");
        }
    }

    private void EnsureValidKey(string key)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(key)
            || !key.StartsWith($"{Prefix}/", StringComparison.Ordinal)
            || key.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The file key is invalid.", nameof(key));
        }
    }

    private static string GetFileName(string key)
    {
        var encodedName = key[(key.LastIndexOf('/') + 1)..];
        return Uri.UnescapeDataString(encodedName);
    }

    private static string EncodeMetadata(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeMetadata(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private HttpRequestException CreateStorageException(AmazonS3Exception exception, string key)
    {
        logger.LogWarning(exception, "Arvan request failed for {Key} with {StatusCode}.", key, exception.StatusCode);
        return new HttpRequestException(
            $"Arvan storage request failed with {(int)exception.StatusCode} {exception.Message}.",
            exception);
    }
}

file sealed class CompositeDisposable(params IDisposable[] resources) : IDisposable
{
    public void Dispose()
    {
        foreach (var resource in resources) resource.Dispose();
    }
}
