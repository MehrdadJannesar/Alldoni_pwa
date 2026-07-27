using System.ComponentModel.DataAnnotations;
using Alldoni.Shared.Security;
using Commandoni.Data;
using Commandoni.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Commandoni.Pages;

public class IndexModel(CommandLibraryDbContext db, SecureValueProtector secureValues) : PageModel
{
    private static readonly int[] AllowedPageSizes = [5, 10, 20, 50];

    [BindProperty]
    public CommandSnippetForm Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    public IReadOnlyList<CommandSnippet> Snippets { get; private set; } = [];

    public IReadOnlyList<string> Categories { get; private set; } = [];

    public IReadOnlyList<int> PageSizes => AllowedPageSizes;

    public int TotalItems { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public int FirstItemNumber => TotalItems == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber => Math.Min(PageNumber * PageSize, TotalItems);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public bool IsEditing => EditId.HasValue;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageDataAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        var snippet = new CommandSnippet
        {
            Name = Input.Name.Trim(),
            Category = Input.Category.Trim(),
            Description = secureValues.Protect(Input.Description),
            Content = secureValues.Protect(Input.Content),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.CommandSnippets.Add(snippet);
        await db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Saved.";
        return RedirectToPage(new { Search, Category, PageSize });
    }

    public async Task<IActionResult> OnPostUpdateAsync(CancellationToken cancellationToken)
    {
        if (!EditId.HasValue)
        {
            return RedirectToPage(new { Search, Category, PageNumber, PageSize });
        }

        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync(cancellationToken);
            return Page();
        }

        var snippet = await db.CommandSnippets.FindAsync([EditId.Value], cancellationToken);
        if (snippet is null)
        {
            StatusMessage = "Command not found.";
            return RedirectToPage(new { Search, Category, PageNumber, PageSize });
        }

        snippet.Name = Input.Name.Trim();
        snippet.Category = Input.Category.Trim();
        snippet.Description = secureValues.Protect(Input.Description);
        snippet.Content = secureValues.Protect(Input.Content);
        snippet.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Updated.";
        return RedirectToPage(new { Search, Category, PageNumber, PageSize });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var snippet = await db.CommandSnippets.FindAsync([id], cancellationToken);
        if (snippet is not null)
        {
            db.CommandSnippets.Remove(snippet);
            await db.SaveChangesAsync(cancellationToken);
            StatusMessage = "Deleted.";
        }

        return RedirectToPage(new { Search, Category, PageNumber, PageSize });
    }

    private async Task LoadPageDataAsync(CancellationToken cancellationToken)
    {
        PageSize = AllowedPageSizes.Contains(PageSize) ? PageSize : 10;
        PageNumber = Math.Max(PageNumber, 1);

        Categories = await db.CommandSnippets
            .AsNoTracking()
            .Select(snippet => snippet.Category)
            .Distinct()
            .OrderBy(category => category)
            .ToListAsync(cancellationToken);

        var query = db.CommandSnippets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Category))
        {
            var categoryFilter = Category.Trim();
            query = query.Where(snippet => snippet.Category == categoryFilter);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var searchPattern = $"%{Search.Trim()}%";
            query = query.Where(snippet =>
                EF.Functions.Like(snippet.Name, searchPattern)
                || EF.Functions.Like(snippet.Category, searchPattern)
                || EF.Functions.Like(snippet.Description, searchPattern)
                || EF.Functions.Like(snippet.Content, searchPattern));
        }

        TotalItems = await query.CountAsync(cancellationToken);
        TotalPages = Math.Max((int)Math.Ceiling(TotalItems / (double)PageSize), 1);
        PageNumber = Math.Min(PageNumber, TotalPages);

        Snippets = await query
            .OrderBy(snippet => snippet.Category)
            .ThenBy(snippet => snippet.Name)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        if (EditId.HasValue)
        {
            var editSnippet = await db.CommandSnippets
                .AsNoTracking()
                .FirstOrDefaultAsync(snippet => snippet.Id == EditId.Value, cancellationToken);

            if (editSnippet is null)
            {
                EditId = null;
                StatusMessage = "Command not found.";
            }
            else
            {
                Input = new CommandSnippetForm
                {
                    Name = editSnippet.Name,
                    Category = editSnippet.Category,
                    Description = secureValues.Unprotect(editSnippet.Description),
                    Content = secureValues.Unprotect(editSnippet.Content)
                };
            }
        }
    }

    public string Fingerprint(string? value) => secureValues.Fingerprint(value);

    public string Plain(string? value) => secureValues.Unprotect(value);

    public class CommandSnippetForm
    {
        [Required, StringLength(120)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(80)]
        public string Category { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required, StringLength(4000)]
        public string Content { get; set; } = string.Empty;
    }
}
