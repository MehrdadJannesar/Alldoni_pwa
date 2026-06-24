namespace Commandoni.Contracts;

public record SnippetResponse(
    int Id,
    string Name,
    string Category,
    string Content,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
