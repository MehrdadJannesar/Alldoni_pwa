using Alldoni.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Alldoni.Pages;

public sealed class IndexModel(
    IOptions<AppDirectoryOptions> options) : PageModel
{
    public IReadOnlyList<AppEntry> Applications { get; private set; } = [];

    public void OnGet()
    {
        Applications = options.Value.Items;
    }
}
