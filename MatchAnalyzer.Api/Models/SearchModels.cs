namespace MatchAnalyzer.Api.Models;

public class SearchRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Team { get; set; }
    public List<FilterCondition> Conditions { get; set; } = new();
}

public class FilterCondition
{
    public bool Enabled { get; set; }
    public string Type { get; set; } = "over"; // "over", "under"
    public string Threshold { get; set; } = "o05"; // "o05", "o15", "o25"
    public string Half { get; set; } = "full"; // "full", "fh", "sh"
    public int MinPercent { get; set; } = 60;
    public int MaxPercent { get; set; } = 100;
}
