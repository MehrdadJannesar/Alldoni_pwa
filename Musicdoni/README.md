# Musicdoni

Musicdoni is a Spotify-style music player built with ASP.NET Core / .NET 10. It stores the music catalog, uploaded tracks, cover images, and playback streams in Arvan Cloud Object Storage.

## Features

- Upload one audio file with optional cover art, or bulk upload multiple audio files at once.
- Stream tracks through the application from Arvan Cloud storage.
- Playlist creation and playlist-specific uploads.
- Delete and remove confirmation with an in-app modal.
- Player controls for play/pause, previous, next, shuffle, repeat one, repeat all, seek, and volume percentage.
- Current playback time and total duration display.
- Cloud-backed catalog stored as JSON in object storage.
- Bucket storage usage display based on summed object sizes.

## Requirements

- .NET SDK 10
- An Arvan Cloud Object Storage bucket
- S3-compatible Arvan access key and secret key

## Solution

This project uses the .NET 10 XML solution format:

```powershell
dotnet build .\Musicdoni.slnx
```

The solution contains:

```xml
<Solution>
  <Project Path="Musicdoni.csproj" />
</Solution>
```

## Configuration

`appsettings.json` is safe to commit and contains only non-secret defaults plus empty placeholders. Use `appsettings.example.json` as a reference for the required Arvan settings.

Recommended local setup uses .NET user secrets:

```powershell
dotnet user-secrets init
dotnet user-secrets set "ArvanStorage:Endpoint" "https://s3.ir-thr-at1.arvanstorage.ir"
dotnet user-secrets set "ArvanStorage:Region" "ir-thr-at1"
dotnet user-secrets set "ArvanStorage:BucketName" "your-bucket-name"
dotnet user-secrets set "ArvanStorage:AccessKey" "your-access-key"
dotnet user-secrets set "ArvanStorage:SecretKey" "your-secret-key"
```

For production, prefer environment variables or your hosting provider secret store:

```powershell
$env:ArvanStorage__Endpoint="https://s3.ir-thr-at1.arvanstorage.ir"
$env:ArvanStorage__Region="ir-thr-at1"
$env:ArvanStorage__BucketName="your-bucket-name"
$env:ArvanStorage__AccessKey="your-access-key"
$env:ArvanStorage__SecretKey="your-secret-key"
```

Optional storage paths:

```json
{
  "ArvanStorage": {
    "CatalogKey": "musicdoni/catalog.json",
    "TracksPrefix": "musicdoni/tracks",
    "CoversPrefix": "musicdoni/covers",
    "StorageLimitBytes": null
  }
}
```

Never commit real access keys, secret keys, or production bucket settings. If a real key has already been committed or shared, rotate it in Arvan Cloud before pushing the repository.

## Run Locally

From the project folder:

```powershell
dotnet run
```

Or specify the local URL:

```powershell
dotnet run --urls "http://127.0.0.1:5099"
```

Then open:

```text
http://127.0.0.1:5099
```

## Build

```powershell
dotnet build .\Musicdoni.slnx
```

## Arvan Object Layout

By default, Musicdoni uses these object keys:

- `musicdoni/catalog.json` for library and playlist metadata
- `musicdoni/tracks/{trackId}.{extension}` for audio files
- `musicdoni/covers/{trackId}.{extension}` for cover images

## Notes

- The bucket name is the Arvan Object Storage bucket/box name, not the user identifier.
- The app streams audio through backend endpoints so browser playback can use byte-range requests correctly.
- Download-manager user agents are blocked, but normal browser media range requests are allowed for playback.


