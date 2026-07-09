using Linkdoni.Models;
using Microsoft.EntityFrameworkCore;

namespace Linkdoni.Data;

public sealed class LinkdoniDbContext(DbContextOptions<LinkdoniDbContext> options) : DbContext(options)
{
    public DbSet<SavedLink> SavedLinks => Set<SavedLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedLink>(entity =>
        {
            entity.HasIndex(link => link.Category);
            entity.Property(link => link.Name).IsRequired().HasMaxLength(120);
            entity.Property(link => link.Url).IsRequired().HasMaxLength(2048);
            entity.Property(link => link.Category).IsRequired().HasMaxLength(80);
            entity.Property(link => link.Description).HasMaxLength(500);
        });
    }
}
