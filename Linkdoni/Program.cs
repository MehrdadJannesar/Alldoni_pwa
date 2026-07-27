using Linkdoni.Data;
using Linkdoni.Models;
using Alldoni.Shared.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddRazorPages();
builder.Services.AddAlldoniSecurity(builder.Environment);
builder.Services.AddDbContext<LinkdoniDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Linkdoni")
        ?? "Data Source=App_Data/linkdoni.db"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<LinkdoniDbContext>().Database.EnsureCreated();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAlldoniSecurity();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

var api = app.MapGroup("/api/links");

api.MapGet("/", async (string? search, string? category, LinkdoniDbContext db) =>
{
    var query = db.SavedLinks.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim();
        query = query.Where(link => link.Name.Contains(term)
            || link.Url.Contains(term)
            || link.Category.Contains(term)
            || (link.Description != null && link.Description.Contains(term)));
    }

    if (!string.IsNullOrWhiteSpace(category))
    {
        query = query.Where(link => link.Category == category);
    }

    return Results.Ok(await query.OrderByDescending(link => link.Id).ToListAsync());
});

api.MapPost("/", async (SavedLink input, LinkdoniDbContext db, SecureValueProtector secureValues) =>
{
    input.Id = 0;
    input.Name = input.Name.Trim();
    input.Url = secureValues.Protect(input.Url);
    input.Category = input.Category.Trim();
    input.Description = secureValues.Protect(input.Description);
    db.SavedLinks.Add(input);
    await db.SaveChangesAsync();
    return Results.Created($"/api/links/{input.Id}", input);
});

api.MapPost("/{id:int}/reveal", async (
    int id,
    RevealRequest request,
    LinkdoniDbContext db,
    PasswordStore passwordStore,
    SecureValueProtector secureValues) =>
{
    if (!passwordStore.Verify(request.Password))
    {
        return Results.Json(new { error = "Password is required." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    var link = await db.SavedLinks.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
    return link is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            link.Id,
            link.Name,
            url = secureValues.Unprotect(link.Url),
            link.Category,
            description = secureValues.Unprotect(link.Description)
        });
});

api.MapPut("/{id:int}", async (int id, SavedLink input, LinkdoniDbContext db, SecureValueProtector secureValues) =>
{
    var link = await db.SavedLinks.FindAsync(id);
    if (link is null) return Results.NotFound();
    link.Name = input.Name.Trim();
    link.Url = secureValues.Protect(input.Url);
    link.Category = input.Category.Trim();
    link.Description = secureValues.Protect(input.Description);
    await db.SaveChangesAsync();
    return Results.Ok(link);
});

api.MapDelete("/{id:int}", async (int id, LinkdoniDbContext db) =>
{
    var link = await db.SavedLinks.FindAsync(id);
    if (link is null) return Results.NotFound();
    db.SavedLinks.Remove(link);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public sealed record RevealRequest(string Password);
