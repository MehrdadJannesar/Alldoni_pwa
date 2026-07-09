namespace Filedoni.Services;

public sealed class ArvanStorageOptions
{
    public const string SectionName = "ArvanStorage";

    public string Endpoint { get; set; } = "https://s3.ir-thr-at1.arvanstorage.ir";
    public string Region { get; set; } = "ir-thr-at1";
    public string BucketName { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string FilesPrefix { get; set; } = "filedoni/files";
    public long MaxUploadBytes { get; set; } = 104_857_600;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(BucketName)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey);
}
