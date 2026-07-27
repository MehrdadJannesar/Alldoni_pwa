using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Musicdoni.Services;

public sealed class ArvanStorageClient : IArvanStorageClient
{
    private readonly ArvanStorageOptions _options;
    private readonly ILogger<ArvanStorageClient> _logger;

    public ArvanStorageClient(
        IOptions<ArvanStorageOptions> options,
        ILogger<ArvanStorageClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PutObjectAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            using var client = CreateClient();
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                InputStream = content,
                ContentType = contentType
            }, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            LogAndThrow(exception, objectKey);
        }
    }

    public async Task<string?> GetTextObjectAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            using var client = CreateClient();
            using var response = await client.GetObjectAsync(_options.BucketName, objectKey, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            LogAndThrow(exception, objectKey);
            return null;
        }
    }

    public Task PutTextObjectAsync(string objectKey, string content, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return PutObjectAsync(objectKey, stream, "application/json", cancellationToken);
    }

    public async Task DeleteObjectAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            using var client = CreateClient();
            await client.DeleteObjectAsync(_options.BucketName, objectKey, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
        catch (AmazonS3Exception exception)
        {
            LogAndThrow(exception, objectKey);
        }
    }

    public async Task<IReadOnlyList<ArvanObjectInfo>> ListObjectsAsync(string? prefix, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            using var client = CreateClient();
            var objects = new List<ArvanObjectInfo>();
            string? token = null;

            do
            {
                var response = await client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _options.BucketName,
                    Prefix = prefix,
                    ContinuationToken = token
                }, cancellationToken);

                var responseObjects = response.S3Objects ?? [];
                objects.AddRange(responseObjects.Select(item =>
                    new ArvanObjectInfo(item.Key, item.Size.GetValueOrDefault(), item.LastModified)));
                token = response.NextContinuationToken;
            }
            while (!string.IsNullOrEmpty(token));

            return objects;
        }
        catch (AmazonS3Exception exception)
        {
            LogAndThrow(exception, prefix ?? "<bucket>");
            return [];
        }
    }

    public async Task<ArvanObjectReadResult> OpenReadObjectAsync(
        string objectKey,
        string? rangeHeader,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        try
        {
            var client = CreateClient();
            var request = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey
            };

            if (TryCreateByteRange(rangeHeader, out var byteRange))
            {
                request.ByteRange = byteRange;
            }

            var response = await client.GetObjectAsync(request, cancellationToken);
            return new ArvanObjectReadResult(
                response.ResponseStream,
                response.Headers.ContentType,
                response.Headers.ContentLength,
                response.Headers["Content-Range"],
                (int)response.HttpStatusCode,
                new CompositeDisposable(response, client));
        }
        catch (AmazonS3Exception exception)
        {
            LogAndThrow(exception, objectKey);
            throw;
        }
    }

    private static bool TryCreateByteRange(string? rangeHeader, out ByteRange? byteRange)
    {
        byteRange = null;
        if (string.IsNullOrWhiteSpace(rangeHeader)
            || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var range = rangeHeader["bytes=".Length..].Split(',', 2)[0].Trim();
        var parts = range.Split('-', 2);
        if (parts.Length != 2
            || string.IsNullOrWhiteSpace(parts[0])
            || !long.TryParse(parts[0], out var start))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            byteRange = new ByteRange(start, long.MaxValue);
            return true;
        }

        if (!long.TryParse(parts[1], out var end) || end < start)
        {
            return false;
        }

        byteRange = new ByteRange(start, end);
        return true;
    }
    public string CreatePresignedGetUrl(string objectKey, TimeSpan lifetime)
    {
        EnsureConfigured();

        using var client = CreateClient();
        return client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime)
        });
    }

    private AmazonS3Client CreateClient()
    {
        var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            ForcePathStyle = true
        };

        return new AmazonS3Client(credentials, config);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint)
            || string.IsNullOrWhiteSpace(_options.BucketName)
            || string.IsNullOrWhiteSpace(_options.AccessKey)
            || string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException(
                "Arvan storage is not configured. Set ArvanStorage:Endpoint, BucketName, AccessKey, and SecretKey.");
        }
    }

    private void LogAndThrow(AmazonS3Exception exception, string objectKey)
    {
        _logger.LogWarning(
            exception,
            "Arvan S3 request for {ObjectKey} failed with {StatusCode}: {Message}",
            objectKey,
            exception.StatusCode,
            exception.Message);
        throw new HttpRequestException(
            $"Arvan storage request failed for '{objectKey}' with {(int)exception.StatusCode} {exception.Message}.",
            exception);
    }
}



file sealed class CompositeDisposable(params IDisposable[] disposables) : IDisposable
{
    public void Dispose()
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }
    }
}



