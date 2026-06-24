using Commandoni.Models;
using Microsoft.EntityFrameworkCore;

namespace Commandoni.Data;

public class CommandLibraryDbContext(DbContextOptions<CommandLibraryDbContext> options) : DbContext(options)
{
    public DbSet<CommandSnippet> CommandSnippets => Set<CommandSnippet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CommandSnippet>(entity =>
        {
            entity.HasKey(snippet => snippet.Id);
            entity.Property(snippet => snippet.Name).HasMaxLength(120).IsRequired();
            entity.Property(snippet => snippet.Category).HasMaxLength(80).IsRequired();
            entity.Property(snippet => snippet.Content).HasMaxLength(4000).IsRequired();
            entity.Property(snippet => snippet.CreatedAtUtc).IsRequired();
            entity.HasIndex(snippet => snippet.Category);
            entity.HasIndex(snippet => snippet.Name);
        });
    }
}
