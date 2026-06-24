using System.ComponentModel.DataAnnotations;

namespace Commandoni.Contracts;

public class CreateSnippetRequest
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Content { get; set; } = string.Empty;
}
