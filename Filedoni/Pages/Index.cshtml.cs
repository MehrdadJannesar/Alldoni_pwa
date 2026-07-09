using System.ComponentModel.DataAnnotations;
using Filedoni.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Filedoni.Pages;

public sealed class IndexModel(
    IFileStorage storage,
    IOptions<ArvanStorageOptions> options,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty, Required, StringLength(160)]
    public string Title { get; set; } = "";

    [BindProperty, Required, StringLength(80)]
    public string Category { get; set; } = "";

    [BindProperty, StringLength(1000)]
    public string? Description { get; set; }

    [BindProperty]
    public EditMetadataInput? Edit { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public IReadOnlyList<StoredFile> Files { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public bool StorageConfigured => options.Value.IsConfigured;
    public long MaxUploadBytes => options.Value.MaxUploadBytes;
    public string? ErrorMessage { get; private set; }
    public bool OpenEditModal { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Title))
        {
            ModelState.AddModelError(nameof(Title), "The Title field is required.");
        }
        else if (Title.Length > 160)
        {
            ModelState.AddModelError(nameof(Title), "Title cannot exceed 160 characters.");
        }

        if (string.IsNullOrWhiteSpace(Category))
        {
            ModelState.AddModelError(nameof(Category), "The Category field is required.");
        }
        else if (Category.Length > 80)
        {
            ModelState.AddModelError(nameof(Category), "Category cannot exceed 80 characters.");
        }

        if (Description?.Length > 1000)
        {
            ModelState.AddModelError(nameof(Description), "Description cannot exceed 1000 characters.");
        }

        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), "Choose a non-empty file.");
        }
        else if (Upload.Length > MaxUploadBytes)
        {
            ModelState.AddModelError(nameof(Upload), $"File size must be under {FormatSize(MaxUploadBytes)}.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var upload = Upload!;
        await using var stream = upload.OpenReadStream();
        await storage.UploadAsync(
            upload.FileName,
            Title,
            Category,
            Description,
            stream,
            upload.ContentType,
            cancellationToken);
        TempData["Message"] = $"{upload.FileName} uploaded.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string key, CancellationToken cancellationToken)
    {
        var download = await storage.OpenReadAsync(key, cancellationToken);
        if (download is null) return NotFound();
        HttpContext.Response.RegisterForDispose(download);
        return new FileStreamResult(download.Content, download.ContentType)
        {
            FileDownloadName = download.FileName,
            EnableRangeProcessing = true
        };
    }

    public async Task<IActionResult> OnPostEditAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        if (Edit is null)
        {
            ModelState.AddModelError(nameof(Edit), "File metadata is required.");
        }
        else
        {
            TryValidateModel(Edit, nameof(Edit));
        }

        if (!ModelState.IsValid)
        {
            OpenEditModal = true;
            await LoadAsync(cancellationToken);
            return Page();
        }

        await storage.UpdateMetadataAsync(
            Edit!.Key,
            Edit.Title,
            Edit.Category,
            Edit.Description,
            cancellationToken);
        TempData["Message"] = "File details updated.";
        return RedirectToPage(new { Search });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string key, CancellationToken cancellationToken)
    {
        await storage.DeleteAsync(key, cancellationToken);
        TempData["Message"] = "File deleted.";
        return RedirectToPage(new { Search });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!StorageConfigured) return;
        try
        {
            var files = await storage.ListAsync(cancellationToken);
            Categories = files.Select(file => file.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category)
                .ToList();
            Files = string.IsNullOrWhiteSpace(Search)
                ? files
                : files.Where(file =>
                    file.FileName.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || file.Title.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || file.Category.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || (file.Description?.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Unable to load files.");
            ErrorMessage = exception.Message;
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    public sealed class EditMetadataInput
    {
        [Required]
        public string Key { get; set; } = "";

        [Required, StringLength(160)]
        public string Title { get; set; } = "";

        [Required, StringLength(80)]
        public string Category { get; set; } = "";

        [StringLength(1000)]
        public string? Description { get; set; }
    }
}
