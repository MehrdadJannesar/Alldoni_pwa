# Commandoni

Commandoni is a lightweight personal command and text snippet library built with ASP.NET Core Razor Pages, Minimal APIs, and SQLite.

It is designed for saving frequently used commands, scripts, notes, and reusable text with a name and category, then quickly searching and copying them when needed.

## Features

- Save commands or text snippets with a name, category, and content.
- Search across names, categories, and snippet text.
- Filter by category.
- Paginated list with selectable page sizes.
- Copy snippets to the clipboard from the UI.
- Delete saved snippets.
- Minimal API endpoints for programmatic access.
- SQLite database storage, created automatically on first run.
- `.slnx` solution format.

## Tech Stack

- .NET 10
- ASP.NET Core Razor Pages
- ASP.NET Core Minimal APIs
- Entity Framework Core
- SQLite
- Bootstrap

## Project Structure

```text
Commandoni.slnx
Commandoni/
  Contracts/       API request and response models
  Data/            EF Core DbContext
  Models/          Command snippet entity
  Pages/           Razor Pages UI
  wwwroot/         CSS, JavaScript, and static assets
```

## Run Locally

```powershell
dotnet restore Commandoni.slnx
dotnet run --project Commandoni\Commandoni.csproj
```

The default launch profile uses:

- HTTP: `http://localhost:5094`
- HTTPS: `https://localhost:7087`

## Build

```powershell
dotnet build Commandoni.slnx
```

## Storage

The SQLite database is created automatically at:

```text
Commandoni/App_Data/commandoni.db
```

Runtime database files and local data-protection keys are intentionally excluded from Git.

## API

The app exposes Minimal API endpoints under `/api/snippets`.

### List snippets

```http
GET /api/snippets
GET /api/snippets?search=dotnet
GET /api/snippets?category=Programming
```

### Get a snippet

```http
GET /api/snippets/{id}
```

### Create a snippet

```http
POST /api/snippets
Content-Type: application/json

{
  "name": "Build solution",
  "category": "Programming",
  "content": "dotnet build Commandoni.slnx"
}
```

### Update a snippet

```http
PUT /api/snippets/{id}
Content-Type: application/json

{
  "name": "Build solution",
  "category": "Programming",
  "content": "dotnet build Commandoni.slnx"
}
```

### Delete a snippet

```http
DELETE /api/snippets/{id}
```
