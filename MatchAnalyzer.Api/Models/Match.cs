namespace MatchAnalyzer.Api.Models;

public class Match
{
    public int Id { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string? Score { get; set; }
    public int? GoalsCount { get; set; }
    public int? FirstHalfGoals { get; set; }
    public bool IsParsed { get; set; }
    
    public string MatchId { get; set; } = string.Empty;
    
    public string HomeTeamId { get; set; } = string.Empty;
    public string AwayTeamId { get; set; } = string.Empty;
}
