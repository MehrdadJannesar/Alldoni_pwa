using System.ComponentModel.DataAnnotations;

namespace Linkdoni.Models;

public sealed class SavedLink
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [Required, StringLength(2048), Url]
    public string Url { get; set; } = "";

    [Required, StringLength(80)]
    public string Category { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
