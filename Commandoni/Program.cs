using Commandoni.Contracts;
using Commandoni.Data;
using Commandoni.Models;
using Alldoni.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var dataProtectionKeysPath = Path.Combine(appDataPath, "Keys");

Directory.CreateDirectory(appDataPath);
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddRazorPages();
builder.Services.AddAlldoniSecurity(builder.Environment);
builder.Services.AddDbContext<CommandLibraryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("CommandLibrary")
        ?? "Data Source=App_Data/commandoni.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CommandLibraryDbContext>();
    db.Database.EnsureCreated();
    EnsureDescriptionColumn(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAlldoniSecurity();

app.MapStaticAssets();

var snippetsApi = app.MapGroup("/api/snippets");

snippetsApi.MapGet("/", async (
    CommandLibraryDbContext db,
    string? search,
    string? category,
    CancellationToken cancellationToken) =>
{
    var query = db.CommandSnippets.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(category))
    {
        var categoryFilter = category.Trim();
        query = query.Where(snippet => snippet.Category == categoryFilter);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        var searchPattern = $"%{search.Trim()}%";
        query = query.Where(snippet =>
            EF.Functions.Like(snippet.Name, searchPattern)
            || EF.Functions.Like(snippet.Category, searchPattern)
            || EF.Functions.Like(snippet.Content, searchPattern));
    }

    var snippets = await query
        .OrderBy(snippet => snippet.Category)
        .ThenBy(snippet => snippet.Name)
        .Select(snippet => new SnippetResponse(
            snippet.Id,
            snippet.Name,
            snippet.Category,
            snippet.Description ?? string.Empty,
            snippet.Content,
            snippet.CreatedAtUtc,
            snippet.UpdatedAtUtc))
        .ToListAsync(cancellationToken);

    return Results.Ok(snippets);
});

snippetsApi.MapGet("/{id:int}", async Task<Results<Ok<SnippetResponse>, NotFound>> (
    int id,
    CommandLibraryDbContext db,
    CancellationToken cancellationToken) =>
{
    var snippet = await db.CommandSnippets
        .AsNoTracking()
        .Where(item => item.Id == id)
        .Select(item => new SnippetResponse(
            item.Id,
            item.Name,
            item.Category,
            item.Description ?? string.Empty,
            item.Content,
            item.CreatedAtUtc,
            item.UpdatedAtUtc))
        .FirstOrDefaultAsync(cancellationToken);

    return snippet is null ? TypedResults.NotFound() : TypedResults.Ok(snippet);
});

snippetsApi.MapPost("/{id:int}/reveal", async (
    int id,
    RevealRequest request,
    CommandLibraryDbContext db,
    PasswordStore passwordStore,
    SecureValueProtector secureValues,
    CancellationToken cancellationToken) =>
{
    if (!passwordStore.Verify(request.Password))
    {
        return Results.Json(new { error = "Password is required." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var snippet = await db.CommandSnippets
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    return snippet is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            snippet.Id,
            name = secureValues.Unprotect(snippet.Name),
            category = secureValues.Unprotect(snippet.Category),
            description = secureValues.Unprotect(snippet.Description),
            content = secureValues.Unprotect(snippet.Content)
        });
});

snippetsApi.MapPost("/", async Task<Created<SnippetResponse>> (
    CreateSnippetRequest request,
    CommandLibraryDbContext db,
    SecureValueProtector secureValues,
    CancellationToken cancellationToken) =>
{
    var snippet = new CommandSnippet
    {
        Name = request.Name.Trim(),
        Category = request.Category.Trim(),
        Description = secureValues.Protect(request.Description),
        Content = secureValues.Protect(request.Content),
        CreatedAtUtc = DateTime.UtcNow
    };

    db.CommandSnippets.Add(snippet);
    await db.SaveChangesAsync(cancellationToken);

    return TypedResults.Created($"/api/snippets/{snippet.Id}", ToResponse(snippet));
});

snippetsApi.MapPut("/{id:int}", async Task<Results<Ok<SnippetResponse>, NotFound>> (
    int id,
    UpdateSnippetRequest request,
    CommandLibraryDbContext db,
    SecureValueProtector secureValues,
    CancellationToken cancellationToken) =>
{
    var snippet = await db.CommandSnippets.FindAsync([id], cancellationToken);
    if (snippet is null)
    {
        return TypedResults.NotFound();
    }

    snippet.Name = request.Name.Trim();
    snippet.Category = request.Category.Trim();
    snippet.Description = secureValues.Protect(request.Description);
    snippet.Content = secureValues.Protect(request.Content);
    snippet.UpdatedAtUtc = DateTime.UtcNow;

    await db.SaveChangesAsync(cancellationToken);

    return TypedResults.Ok(ToResponse(snippet));
});

snippetsApi.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (
    int id,
    CommandLibraryDbContext db,
    CancellationToken cancellationToken) =>
{
    var snippet = await db.CommandSnippets.FindAsync([id], cancellationToken);
    if (snippet is null)
    {
        return TypedResults.NotFound();
    }

    db.CommandSnippets.Remove(snippet);
    await db.SaveChangesAsync(cancellationToken);

    return TypedResults.NoContent();
});

app.MapRazorPages()
   .WithStaticAssets();

app.Run();

static SnippetResponse ToResponse(CommandSnippet snippet) =>
    new(
        snippet.Id,
        snippet.Name,
        snippet.Category,
        snippet.Description ?? string.Empty,
        snippet.Content,
        snippet.CreatedAtUtc,
        snippet.UpdatedAtUtc);

static void EnsureDescriptionColumn(CommandLibraryDbContext db)
{
    var connection = db.Database.GetDbConnection();
    connection.Open();

    var hasDescriptionColumn = false;
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA table_info(CommandSnippets);";

    using (var reader = command.ExecuteReader())
    {
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), nameof(CommandSnippet.Description), StringComparison.OrdinalIgnoreCase))
            {
                hasDescriptionColumn = true;
                break;
            }
        }
    }

    if (!hasDescriptionColumn)
    {
        db.Database.ExecuteSqlRaw("ALTER TABLE CommandSnippets ADD COLUMN Description TEXT NOT NULL DEFAULT '';");
    }
}

public sealed record RevealRequest(string Password);
