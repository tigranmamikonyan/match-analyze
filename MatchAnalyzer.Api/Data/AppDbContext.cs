using Microsoft.EntityFrameworkCore;
using MatchAnalyzer.Api.Models;

namespace MatchAnalyzer.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Match> Matches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Match>()
            .HasIndex(m => m.MatchId)
            .IsUnique();
    }
}
