using Commandoni.Contracts;
using Commandoni.Data;
using Commandoni.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var appDataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var dataProtectionKeysPath = Path.Combine(appDataPath, "Keys");

Directory.CreateDirectory(appDataPath);
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddRazorPages();
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
builder.Services.AddDbContext<CommandLibraryDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("CommandLibrary")
        ?? "Data Source=App_Data/commandoni.db"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CommandLibraryDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

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
            item.Content,
            item.CreatedAtUtc,
            item.UpdatedAtUtc))
        .FirstOrDefaultAsync(cancellationToken);

    return snippet is null ? TypedResults.NotFound() : TypedResults.Ok(snippet);
});

snippetsApi.MapPost("/", async Task<Created<SnippetResponse>> (
    CreateSnippetRequest request,
    CommandLibraryDbContext db,
    CancellationToken cancellationToken) =>
{
    var snippet = new CommandSnippet
    {
        Name = request.Name.Trim(),
        Category = request.Category.Trim(),
        Content = request.Content.Trim(),
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
    CancellationToken cancellationToken) =>
{
    var snippet = await db.CommandSnippets.FindAsync([id], cancellationToken);
    if (snippet is null)
    {
        return TypedResults.NotFound();
    }

    snippet.Name = request.Name.Trim();
    snippet.Category = request.Category.Trim();
    snippet.Content = request.Content.Trim();
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
        snippet.Content,
        snippet.CreatedAtUtc,
        snippet.UpdatedAtUtc);
