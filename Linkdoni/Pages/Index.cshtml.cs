using System.ComponentModel.DataAnnotations;
using Linkdoni.Data;
using Linkdoni.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Linkdoni.Pages;

public sealed class IndexModel(LinkdoniDbContext db) : PageModel
{
    [BindProperty]
    public LinkInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    public IReadOnlyList<SavedLink> Links { get; private set; } = [];
    public IReadOnlyList<string> Categories { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        SavedLink link;
        if (Input.Id is int id)
        {
            link = await db.SavedLinks.FindAsync(id) ?? new SavedLink();
            if (link.Id == 0) db.SavedLinks.Add(link);
        }
        else
        {
            link = new SavedLink();
            db.SavedLinks.Add(link);
        }

        link.Name = Input.Name.Trim();
        link.Url = Input.Url.Trim();
        link.Category = Input.Category.Trim();
        link.Description = Input.Description?.Trim();
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var link = await db.SavedLinks.FindAsync(id);
        if (link is not null)
        {
            db.SavedLinks.Remove(link);
            await db.SaveChangesAsync();
        }
        return RedirectToPage(new { Search, Category, PageNumber, PageSize });
    }

    private async Task LoadAsync()
    {
        PageSize = new[] { 5, 10, 20, 50 }.Contains(PageSize) ? PageSize : 10;
        PageNumber = Math.Max(1, PageNumber);
        Categories = await db.SavedLinks.AsNoTracking()
            .Select(link => link.Category).Distinct().OrderBy(value => value).ToListAsync();

        var query = db.SavedLinks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(link => link.Name.Contains(term)
                || link.Url.Contains(term)
                || link.Category.Contains(term)
                || (link.Description != null && link.Description.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(Category))
        {
            query = query.Where(link => link.Category == Category);
        }

        TotalCount = await query.CountAsync();
        PageNumber = Math.Min(PageNumber, TotalPages);
        Links = await query.OrderByDescending(link => link.Id)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
    }

    public sealed class LinkInput
    {
        public int? Id { get; set; }
        [Required, StringLength(120)] public string Name { get; set; } = "";
        [Required, StringLength(2048), Url] public string Url { get; set; } = "";
        [Required, StringLength(80)] public string Category { get; set; } = "";
        [StringLength(500)] public string? Description { get; set; }
    }
}
