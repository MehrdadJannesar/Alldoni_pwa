namespace Commandoni.Contracts;

public record SnippetResponse(
    int Id,
    string Name,
    string Category,
    string Description,
    string Content,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
