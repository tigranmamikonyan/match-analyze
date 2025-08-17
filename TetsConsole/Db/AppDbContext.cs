using Microsoft.EntityFrameworkCore;

namespace TetsConsole.Db;

public class AppDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(
            @"Host=127.0.0.1;Port=5432;Database=football;Username=postgres;Password=tiko400090;");
    }
    
    public DbSet<Match> Matches { get; set; }
}

public class Match
{
    public int Id { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public DateTime? Date { get; set; }
    public string? Score { get; set; }
    public int? GoalsCount { get; set; }
    public bool IsParsed { get; set; }
    
    public string MatchId { get; set; }
}
