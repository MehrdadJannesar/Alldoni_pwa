using Alldoni.Models;
using Alldoni.Services;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
var keysDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Keys");
Directory.CreateDirectory(keysDirectory);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("Alldoni");
builder.Services.Configure<AppDirectoryOptions>(
    builder.Configuration.GetSection(AppDirectoryOptions.SectionName));
builder.Services.AddScoped<AppAvailabilityService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapGet("/api/apps", async (
    Microsoft.Extensions.Options.IOptions<AppDirectoryOptions> options,
    AppAvailabilityService availability,
    CancellationToken cancellationToken) =>
{
    var states = await availability.CheckAsync(options.Value.Items, cancellationToken);
    return Results.Ok(options.Value.Items.Select(application => new
    {
        application.Key,
        application.Name,
        application.Description,
        application.Storage,
        application.Url,
        available = states.GetValueOrDefault(application.Key)
    }));
});

app.Run();
