using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alldoni.Shared.Security;

public static class AlldoniSecurity
{
    public const string Scheme = "AlldoniCookie";
    public const string UserName = "admin";
    private const string HubLoginUrl = "http://localhost:5051/login";

    public static IServiceCollection AddAlldoniSecurity(this IServiceCollection services, IHostEnvironment environment)
    {
        var workspaceRoot = Directory.GetParent(environment.ContentRootPath)?.FullName ?? environment.ContentRootPath;
        var appDataPath = Path.Combine(workspaceRoot, "App_Data");
        var keysPath = Path.Combine(appDataPath, "Keys");
        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(keysPath);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("AlldoniPwa");
        services.AddSingleton<PasswordStore>();
        services.AddSingleton<SecureValueProtector>();
        services.AddAuthentication(Scheme)
            .AddCookie(Scheme, options =>
            {
                options.Cookie.Name = ".Alldoni.Auth";
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
            });
        services.AddAuthorization();
        return services;
    }

    public static WebApplication UseAlldoniSecurity(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (IsPublicPath(context.Request.Path))
            {
                await next();
                return;
            }

            if (context.User.Identity?.IsAuthenticated != true)
            {
                var returnUrl = CreateCurrentUrl(context.Request);
                var loginUrl = IsHubRequest(context.Request)
                    ? $"/login?returnUrl={UrlEncoder.Default.Encode(context.Request.PathBase + context.Request.Path + context.Request.QueryString)}"
                    : $"{HubLoginUrl}?returnUrl={UrlEncoder.Default.Encode(returnUrl)}";
                context.Response.Redirect(loginUrl);
                return;
            }

            var store = context.RequestServices.GetRequiredService<PasswordStore>();
            if (store.MustChangePassword && !context.Request.Path.StartsWithSegments("/change-password"))
            {
                context.Response.Redirect("/change-password");
                return;
            }

            await next();
        });
        app.UseAuthorization();
        app.MapAlldoniSecurityEndpoints();
        return app;
    }

    public static bool IsPublicPath(PathString path)
    {
        return path.StartsWithSegments("/login")
            || path.StartsWithSegments("/change-password")
            || path.StartsWithSegments("/css")
            || path.StartsWithSegments("/js")
            || path.StartsWithSegments("/lib")
            || path.StartsWithSegments("/fonts")
            || path.StartsWithSegments("/manifest.webmanifest")
            || path.StartsWithSegments("/service-worker.js")
            || path.StartsWithSegments("/favicon.ico");
    }

    public static IEndpointRouteBuilder MapAlldoniSecurityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/login", (HttpContext context) =>
        {
            var request = context.Request;
            var returnUrl = request.Query["returnUrl"].ToString();

            if (!IsHubRequest(request))
            {
                var target = string.IsNullOrWhiteSpace(returnUrl)
                    ? CreateAppRootUrl(request)
                    : returnUrl;
                return Results.Redirect($"{HubLoginUrl}?returnUrl={UrlEncoder.Default.Encode(target)}");
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                return Results.Redirect(SafeReturnUrl(returnUrl));
            }

            return Results.Content(RenderLogin(returnUrl), "text/html; charset=utf-8");
        });

        endpoints.MapPost("/login", async (HttpContext context, PasswordStore store) =>
        {
            var form = await context.Request.ReadFormAsync();
            var password = form["password"].ToString();
            if (!store.Verify(password))
            {
                return Results.Content(RenderLogin(form["returnUrl"].ToString(), "The password is incorrect."), "text/html; charset=utf-8");
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, UserName)], Scheme);
            await context.SignInAsync(Scheme, new ClaimsPrincipal(identity));
            var returnUrl = form["returnUrl"].ToString();
            return Results.Redirect(store.MustChangePassword ? "/change-password" : SafeReturnUrl(returnUrl));
        }).DisableAntiforgery();

        endpoints.MapGet("/change-password", () =>
            Results.Content(RenderChangePassword(), "text/html; charset=utf-8"));

        endpoints.MapPost("/change-password", async (HttpContext context, PasswordStore store) =>
        {
            var form = await context.Request.ReadFormAsync();
            var current = form["currentPassword"].ToString();
            var next = form["newPassword"].ToString();
            var confirm = form["confirmPassword"].ToString();

            if (!store.Verify(current))
            {
                return Results.Content(RenderChangePassword("The current password is incorrect."), "text/html; charset=utf-8");
            }

            if (next.Length < 6 || next != confirm || next == "123456")
            {
                return Results.Content(RenderChangePassword("The new password must be at least 6 characters, match its confirmation, and not be 123456."), "text/html; charset=utf-8");
            }

            store.ChangePassword(next);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        endpoints.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();

        endpoints.MapPost("/api/security/verify-password", async (HttpRequest request, PasswordStore store) =>
        {
            var body = await JsonSerializer.DeserializeAsync<PasswordCheck>(request.Body, JsonOptions);
            return body is not null && store.Verify(body.Password)
                ? Results.Ok(new { ok = true })
                : Results.Json(new { ok = false, error = "Password is invalid." }, statusCode: StatusCodes.Status401Unauthorized);
        });

        return endpoints;
    }

    public static IResult RequirePassword(string? password, PasswordStore store)
    {
        return store.Verify(password ?? string.Empty)
            ? Results.Ok()
            : Results.Json(new { error = "Password is required." }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
        {
            return returnUrl;
        }

        return Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                ? returnUrl
                : "/";
    }

    private static string CreateCurrentUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";

    private static string CreateAppRootUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}/";

    private static bool IsHubRequest(HttpRequest request) =>
        request.Host.Port == 5051;

    private static string RenderLogin(string? returnUrl = null, string? error = null) => RenderShell("Sign in", $"""
        <form method="post" action="/login" class="auth-card">
          <h1>Sign in to Alldoni</h1>
          <p>Fixed user: <strong>admin</strong></p>
          {(string.IsNullOrWhiteSpace(error) ? "" : $"<div class=\"auth-error\">{HtmlEncoder.Default.Encode(error)}</div>")}
          <input type="hidden" name="returnUrl" value="{HtmlEncoder.Default.Encode(returnUrl ?? "/")}" />
          <label>Password<input name="password" type="password" autocomplete="current-password" autofocus required /></label>
          <button type="submit">Sign in</button>
        </form>
        """);

    private static string RenderChangePassword(string? error = null) => RenderShell("Change password", $"""
        <form method="post" action="/change-password" class="auth-card">
          <h1>Change your password</h1>
          <p>The initial password 123456 is for first-time setup only.</p>
          {(string.IsNullOrWhiteSpace(error) ? "" : $"<div class=\"auth-error\">{HtmlEncoder.Default.Encode(error)}</div>")}
          <label>Current password<input name="currentPassword" type="password" autocomplete="current-password" required /></label>
          <label>New password<input name="newPassword" type="password" autocomplete="new-password" required /></label>
          <label>Confirm new password<input name="confirmPassword" type="password" autocomplete="new-password" required /></label>
          <button type="submit">Save password</button>
        </form>
        """);

    private static string RenderShell(string title, string body) => $$"""
        <!doctype html><html lang="en" dir="ltr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{title}}</title><style>
        @font-face{font-family:IRANSans;src:url('/fonts/IRANSans/IRANSansWeb(FaNum).woff') format('woff');font-weight:400;font-style:normal;font-display:swap;unicode-range:U+0000-002F,U+003A-10FFFF}
        @font-face{font-family:IRANSans;src:url('/fonts/IRANSans/IRANSansWeb(FaNum)_Bold.woff') format('woff');font-weight:700;font-style:normal;font-display:swap;unicode-range:U+0000-002F,U+003A-10FFFF}
        body{margin:0;min-height:100vh;display:grid;place-items:center;font-family:IRANSans,Tahoma,Arial,sans-serif;background:#101820;color:#f7f7f7}
        .auth-card{width:min(420px,calc(100% - 32px));display:grid;gap:14px;background:#fff;color:#17212b;padding:28px;border-radius:8px;box-shadow:0 24px 80px #0008}
        h1{margin:0;font-size:1.6rem}p{margin:0;color:#586575}label{display:grid;gap:6px;font-weight:700}input{padding:12px;border:1px solid #cfd7df;border-radius:6px;font:inherit}
        button{padding:12px;border:0;border-radius:6px;background:#0f766e;color:white;font:inherit;font-weight:800;cursor:pointer}.auth-error{padding:10px;border-radius:6px;background:#fee2e2;color:#991b1b}
        </style></head><body>{{body}}</body></html>
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static JsonSerializerOptions JsonOptionsForStore { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record PasswordCheck(string Password);
}

public sealed class PasswordStore(IHostEnvironment environment)
{
    private readonly string path = Path.Combine(Directory.GetParent(environment.ContentRootPath)?.FullName ?? environment.ContentRootPath, "App_Data", "user.json");
    private readonly object gate = new();

    public bool MustChangePassword => Load().MustChange;

    public bool Verify(string password)
    {
        var record = Load();
        var hash = Hash(password, Convert.FromBase64String(record.Salt));
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(record.Hash), hash);
    }

    public void ChangePassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        Save(new PasswordRecord(Convert.ToBase64String(salt), Convert.ToBase64String(Hash(password, salt)), false));
    }

    private PasswordRecord Load()
    {
        lock (gate)
        {
            if (!File.Exists(path))
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                var initial = new PasswordRecord(Convert.ToBase64String(salt), Convert.ToBase64String(Hash("123456", salt)), true);
                Save(initial);
                return initial;
            }

            return JsonSerializer.Deserialize<PasswordRecord>(File.ReadAllText(path), AlldoniSecurity.JsonOptionsForStore) ?? throw new InvalidOperationException("Invalid user store.");
        }
    }

    private void Save(PasswordRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(record, AlldoniSecurity.JsonOptionsForStore));
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

    private sealed record PasswordRecord(string Salt, string Hash, bool MustChange);
}

public sealed class SecureValueProtector(IDataProtectionProvider provider)
{
    private const string Prefix = "alldoni:v1:";
    private readonly IDataProtector protector = provider.CreateProtector("secure-values");

    public string Protect(string? value)
    {
        var plain = value?.Trim() ?? string.Empty;
        return plain.StartsWith(Prefix, StringComparison.Ordinal) ? plain : Prefix + protector.Protect(plain);
    }

    public string Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.StartsWith(Prefix, StringComparison.Ordinal)
            ? protector.Unprotect(value[Prefix.Length..])
            : value;
    }

    public string Fingerprint(string? value)
    {
        var plain = Unprotect(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
