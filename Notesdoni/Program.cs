using System.Text;
using Notesdoni.Services;
using Alldoni.Shared.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorPages();
builder.Services.AddAlldoniSecurity(builder.Environment);
builder.Services.Configure<ArvanStorageOptions>(
    builder.Configuration.GetSection(ArvanStorageOptions.SectionName));
builder.Services.AddSingleton<IFileStorage, ArvanFileStorage>();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = 104_857_600);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAlldoniSecurity();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/api/status", (IOptions<ArvanStorageOptions> options) =>
    Results.Ok(new
    {
        storage = "Arvan Cloud Object Storage",
        configured = options.Value.IsConfigured,
        bucketConfigured = !string.IsNullOrWhiteSpace(options.Value.BucketName)
    }));

app.MapGet("/api/files", async (IFileStorage storage, CancellationToken cancellationToken) =>
    Results.Ok(await storage.ListAsync(cancellationToken)));

app.MapPost("/api/files/reveal", async (
    RevealFileRequest request,
    IFileStorage storage,
    PasswordStore passwordStore,
    SecureValueProtector secureValues,
    CancellationToken cancellationToken) =>
{
    if (!passwordStore.Verify(request.Password))
    {
        return Results.Json(new { error = "Password is required." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var file = (await storage.ListAsync(cancellationToken))
        .FirstOrDefault(item => item.Key == request.Key);

    return file is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            file.Key,
            fileName = secureValues.Unprotect(file.FileName),
            title = secureValues.Unprotect(file.Title),
            file.Category,
            description = secureValues.Unprotect(file.Description),
            file.HasAttachment,
            file.Size,
            file.LastModified
        });
});

app.MapPost("/api/files", async (
    [Microsoft.AspNetCore.Mvc.FromForm] IFormFile? file,
    [Microsoft.AspNetCore.Mvc.FromForm] string title,
    [Microsoft.AspNetCore.Mvc.FromForm] string category,
    [Microsoft.AspNetCore.Mvc.FromForm] string? description,
    IFileStorage storage,
    IOptions<ArvanStorageOptions> options,
    SecureValueProtector secureValues,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(title)) return Results.BadRequest(new { error = "Title is required." });
    if (string.IsNullOrWhiteSpace(category)) return Results.BadRequest(new { error = "Category is required." });
    if (file is not null && file.Length > options.Value.MaxUploadBytes)
        return Results.BadRequest(new { error = "The file exceeds the configured upload limit." });

    await using var stream = file is { Length: > 0 }
        ? file.OpenReadStream()
        : NoteFileDefaults.CreateStream(title, description);
    var fileName = file is { Length: > 0 }
        ? file.FileName
        : NoteFileDefaults.CreateFileName(title);
    var contentType = file is { Length: > 0 }
        ? file.ContentType
        : "text/plain";
    var hasAttachment = file is { Length: > 0 };

    await storage.UploadAsync(
        fileName,
        secureValues.Protect(title),
        category,
        secureValues.Protect(description),
        hasAttachment,
        stream,
        contentType,
        cancellationToken);
    return Results.Created("/api/files", new { fileName, title, category, description, length = file?.Length ?? stream.Length });
}).DisableAntiforgery();

app.MapGet("/api/files/download", async (
    string key,
    string password,
    IFileStorage storage,
    PasswordStore passwordStore,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (!passwordStore.Verify(password))
    {
        return Results.Json(new { error = "Password is required." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var file = (await storage.ListAsync(cancellationToken))
        .FirstOrDefault(item => item.Key == key);
    if (file is null || !file.HasAttachment) return Results.NotFound();

    var download = await storage.OpenReadAsync(key, cancellationToken);
    if (download is null) return Results.NotFound();
    context.Response.RegisterForDispose(download);
    return Results.File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: true);
});

app.MapPut("/api/files/metadata", async (
    UpdateFileMetadataRequest request,
    IFileStorage storage,
    SecureValueProtector secureValues,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Key))
        return Results.BadRequest(new { error = "File key is required." });
    if (string.IsNullOrWhiteSpace(request.Title))
        return Results.BadRequest(new { error = "Title is required." });
    if (string.IsNullOrWhiteSpace(request.Category))
        return Results.BadRequest(new { error = "Category is required." });

    await storage.UpdateMetadataAsync(
        request.Key,
        secureValues.Protect(request.Title),
        request.Category,
        secureValues.Protect(request.Description),
        cancellationToken);
    return Results.NoContent();
});

app.MapDelete("/api/files", async (
    string key,
    IFileStorage storage,
    CancellationToken cancellationToken) =>
{
    await storage.DeleteAsync(key, cancellationToken);
    return Results.NoContent();
});

app.Run();

public sealed record UpdateFileMetadataRequest(
    string Key,
    string Title,
    string Category,
    string? Description);

public sealed record RevealFileRequest(string Key, string Password);

file static class NoteFileDefaults
{
    public static MemoryStream CreateStream(string title, string? description)
    {
        var content = string.IsNullOrWhiteSpace(description)
            ? title.Trim()
            : description.Trim();
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }

    public static string CreateFileName(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeTitle = new string(title.Trim()
            .Select(character => invalidChars.Contains(character) ? '-' : character)
            .ToArray());
        safeTitle = string.Join('-', safeTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{(string.IsNullOrWhiteSpace(safeTitle) ? "note" : safeTitle)}.txt";
    }
}
