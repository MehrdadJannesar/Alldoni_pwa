using System.Text.Json;
using Alldoni.Shared.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddAlldoniSecurity(builder.Environment);
builder.Services.AddSingleton<PasswordVault>();

var app = builder.Build();
app.UseHttpsRedirection();
app.UseRouting();
app.UseAlldoniSecurity();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/passwords", (PasswordVault vault, SecureValueProtector secureValues) =>
    Results.Ok(vault.List().Select(item => new
    {
        item.Id,
        title = secureValues.Unprotect(item.Title),
        username = secureValues.Fingerprint(item.Username),
        password = secureValues.Fingerprint(item.Password),
        note = secureValues.Fingerprint(item.Note),
        item.CreatedAtUtc
    })));

app.MapPost("/api/passwords", (PasswordInput input, PasswordVault vault, SecureValueProtector secureValues) =>
{
    if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Password))
    {
        return Results.BadRequest(new { error = "Title and password are required." });
    }

    var item = new PasswordItem(
        Guid.NewGuid(),
        secureValues.Protect(input.Title),
        secureValues.Protect(input.Username),
        secureValues.Protect(input.Password),
        secureValues.Protect(input.Note),
        DateTime.UtcNow);
    vault.Add(item);
    return Results.Created($"/api/passwords/{item.Id}", new { item.Id });
});

app.MapPost("/api/passwords/{id:guid}/reveal", (Guid id, RevealRequest request, PasswordVault vault, PasswordStore store, SecureValueProtector secureValues) =>
{
    if (!store.Verify(request.Password))
    {
        return Results.Json(new { error = "Password is required." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var item = vault.List().FirstOrDefault(value => value.Id == id);
    return item is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            item.Id,
            title = secureValues.Unprotect(item.Title),
            username = secureValues.Unprotect(item.Username),
            password = secureValues.Unprotect(item.Password),
            note = secureValues.Unprotect(item.Note)
        });
});

app.MapDelete("/api/passwords/{id:guid}", (Guid id, PasswordVault vault) =>
{
    vault.Delete(id);
    return Results.NoContent();
});

app.MapFallbackToFile("index.html");
app.Run();

public sealed record PasswordInput(string Title, string? Username, string Password, string? Note);
public sealed record RevealRequest(string Password);
public sealed record PasswordItem(Guid Id, string Title, string? Username, string Password, string? Note, DateTime CreatedAtUtc);

public sealed class PasswordVault(IHostEnvironment environment)
{
    private readonly string path = Path.Combine(environment.ContentRootPath, "App_Data", "passwords.json");
    private readonly object gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public IReadOnlyList<PasswordItem> List()
    {
        lock (gate)
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<PasswordItem>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
    }

    public void Add(PasswordItem item)
    {
        lock (gate)
        {
            var items = List().ToList();
            items.Insert(0, item);
            Save(items);
        }
    }

    public void Delete(Guid id)
    {
        lock (gate)
        {
            Save(List().Where(item => item.Id != id).ToList());
        }
    }

    private void Save(IReadOnlyList<PasswordItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(items, JsonOptions));
    }
}
