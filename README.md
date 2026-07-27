# Alldoni

![Alldoni application hub](docs/alldoni-preview.png)

Alldoni is a .NET 10 workspace containing a central Super App and six small personal library applications:

- **Alldoni** is the central hub for opening and checking the availability of every application.
- **Commandoni** stores searchable commands and text snippets in SQLite.
- **Linkdoni** stores searchable links with names, categories, and optional descriptions in SQLite.
- **Filedoni** uploads, lists, downloads, searches, and deletes files in Arvan Cloud Object Storage.
- **Notesdoni** uploads, lists, downloads, edits, searches, and deletes note files in Arvan Cloud Object Storage.
- **Passworddoni** stores encrypted password records and reveals them after the admin password is confirmed.
- **Musicdoni** manages and plays a private music library stored in Arvan Cloud Object Storage.

All projects use Razor Pages for their interfaces. Commandoni and Linkdoni also expose Minimal API endpoints.

## Projects

| Project | Storage | Purpose |
| --- | --- | --- |
| `Alldoni/` | Configuration | Central application hub |
| `Commandoni/` | SQLite | Commands and reusable text |
| `Linkdoni/` | SQLite | Important links and bookmarks |
| `Filedoni/` | Arvan S3-compatible storage | Private file storage |
| `Notesdoni/` | Arvan S3-compatible storage | Note file storage |
| `Passworddoni/` | Encrypted JSON | Private password vault |
| `Musicdoni/` | Arvan S3-compatible storage | Private music library |

The parent solution is `Alldoni.slnx`.

## Requirements

- .NET 10 SDK
- An Arvan Cloud Object Storage bucket for Filedoni and Notesdoni
- S3-compatible Arvan access and secret keys

## Build

```powershell
dotnet restore Alldoni.slnx
dotnet build Alldoni.slnx
```

## Run

Run each application in a separate terminal:

```powershell
dotnet run --project Alldoni\Alldoni.csproj --urls http://localhost:5051
dotnet run --project Commandoni\Commandoni.csproj --urls http://localhost:5095
dotnet run --project Linkdoni\Linkdoni.csproj --urls http://localhost:5166
dotnet run --project Filedoni\Filedoni.csproj --urls http://localhost:5277
dotnet run --project Notesdoni\Notesdoni.csproj --urls http://localhost:5388
dotnet run --project Passworddoni\Passworddoni.csproj --urls http://localhost:5489
dotnet run --project Musicdoni\Musicdoni.csproj --urls http://localhost:5100
```

Open the Super App at `http://localhost:5051`. Its application URLs can be changed in `Alldoni/appsettings.json` or overridden through configuration.

Commandoni and Linkdoni create their `App_Data` directories and SQLite databases automatically on first run.

## Configure Filedoni and Notesdoni

Keep real credentials outside `appsettings.json`. For local development:

```powershell
dotnet user-secrets --project Filedoni\Filedoni.csproj set "ArvanStorage:Endpoint" "https://s3.ir-thr-at1.arvanstorage.ir"
dotnet user-secrets --project Filedoni\Filedoni.csproj set "ArvanStorage:Region" "ir-thr-at1"
dotnet user-secrets --project Filedoni\Filedoni.csproj set "ArvanStorage:BucketName" "your-bucket-name"
dotnet user-secrets --project Filedoni\Filedoni.csproj set "ArvanStorage:AccessKey" "your-access-key"
dotnet user-secrets --project Filedoni\Filedoni.csproj set "ArvanStorage:SecretKey" "your-secret-key"
dotnet user-secrets --project Notesdoni\Notesdoni.csproj set "ArvanStorage:Endpoint" "https://s3.ir-thr-at1.arvanstorage.ir"
dotnet user-secrets --project Notesdoni\Notesdoni.csproj set "ArvanStorage:Region" "ir-thr-at1"
dotnet user-secrets --project Notesdoni\Notesdoni.csproj set "ArvanStorage:BucketName" "your-bucket-name"
dotnet user-secrets --project Notesdoni\Notesdoni.csproj set "ArvanStorage:AccessKey" "your-access-key"
dotnet user-secrets --project Notesdoni\Notesdoni.csproj set "ArvanStorage:SecretKey" "your-secret-key"
```

For a deployed instance, define the equivalent environment variables:

```text
ArvanStorage__Endpoint
ArvanStorage__Region
ArvanStorage__BucketName
ArvanStorage__AccessKey
ArvanStorage__SecretKey
```

Filedoni uses the `filedoni/files` prefix. Notesdoni uses the `notesdoni/files` prefix, allowing both apps to share a bucket without mixing their objects.

## Deploy Filedoni

The project includes a production Dockerfile:

```powershell
docker build -f Filedoni\Dockerfile -t filedoni .
docker run --rm -p 8080:8080 `
  -e ArvanStorage__BucketName="your-bucket-name" `
  -e ArvanStorage__AccessKey="your-access-key" `
  -e ArvanStorage__SecretKey="your-secret-key" `
  filedoni
```

Deploy this image to an Arvan container application and configure the five `ArvanStorage__...` environment variables in the application settings. Do not bake credentials into the image.

## API

- Commandoni: `/api/snippets`
- Linkdoni: `/api/links`
- Filedoni status: `/api/status`
- Filedoni files: `/api/files`
- Notesdoni status: `/api/status`
- Notesdoni files: `/api/files`

Swagger is intentionally not enabled; the APIs are small companions to the Razor interfaces.
