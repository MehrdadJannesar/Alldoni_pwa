namespace Musicdoni.Services;

public sealed class ArvanStorageOptions
{
    public const string SectionName = "ArvanStorage";

    public string Endpoint { get; set; } = "https://s3.ir-thr-at1.arvanstorage.ir";
    public string Region { get; set; } = "ir-thr-at1";
    public string BucketName { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string CatalogKey { get; set; } = "musicdoni/catalog.json";
    public string TracksPrefix { get; set; } = "musicdoni/tracks";
    public string CoversPrefix { get; set; } = "musicdoni/covers";
    public long? StorageLimitBytes { get; set; }
}


