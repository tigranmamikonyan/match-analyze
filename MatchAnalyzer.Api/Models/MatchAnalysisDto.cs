namespace MatchAnalyzer.Api.Models;

public class MatchAnalysisDto
{
    public required List<YearlyStats> Stats { get; set; }
    public required PredictionStats Predictions { get; set; }
}

public class YearlyStats
{
    public int Year { get; set; }
    public required string Team { get; set; } // "HomeTeamName", "AwayTeamName", or "H2H Games"
    public int TotalGames { get; set; }
    public int Over05 { get; set; }
    public int Over15 { get; set; }
    public int Over25 { get; set; }
    
    public int OverFH05 { get; set; } // First Half
    public int OverFH15 { get; set; }
    public int OverFH25 { get; set; }
}

public class PredictionStats
{
    public string FullTime { get; set; } = string.Empty; // e.g. "≥0.5 = 92%, ≥1.5 = 79%"
    public string FirstHalf { get; set; } = string.Empty;
    public string SecondHalf { get; set; } = string.Empty;
}
