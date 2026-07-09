namespace Alldoni.Models;

public sealed class AppDirectoryOptions
{
    public const string SectionName = "Applications";
    public List<AppEntry> Items { get; set; } = [];
}

public sealed class AppEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Storage { get; set; } = "";
    public string Url { get; set; } = "";
}
