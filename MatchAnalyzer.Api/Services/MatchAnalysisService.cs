using MatchAnalyzer.Api.Data;
using MatchAnalyzer.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MatchAnalyzer.Api.Services;

public class MatchAnalysisService
{
    private readonly AppDbContext _context;

    public MatchAnalysisService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<MatchAnalysisDto?> AnalyzeMatchAsync(int matchId)
    {
        var match = await _context.Matches.FindAsync(matchId);
        if (match == null) return null;
        
        return await AnalyzeMatchAsync(match);
    }

    // --- Prediction Helpers ---
    private double SmoothedRate(int success, int total, double alpha = 1.0, double beta = 1.0)
        => total <= 0 ? 0.5 : (success + alpha) / (total + alpha + beta);

    private double WeightFromSamples(int n, int cap) => Math.Min(Math.Max(n, 0), cap) / (double)cap;

    private double Blend(params (double p, double w)[] terms)
    {
        double sw = terms.Sum(t => t.w);
        if (sw <= 0) return 0.5;
        return terms.Sum(t => t.p * t.w) / sw;
    }

    private string ToPct(double p) => $"{Math.Round(p * 100)}%";


    public async Task<MatchAnalysisDto> AnalyzeMatchAsync(Match match)
    {
        var results = await AnalyzeMatchesBulkAsync(new List<Match> { match });
        return results[match];
    }

    public async Task<Dictionary<Match, MatchAnalysisDto>> AnalyzeMatchesBulkAsync(List<Match> matches)
    {
        var result = new Dictionary<Match, MatchAnalysisDto>();
        if (matches.Count == 0) return result;

        // 1. Collect all Team IDs
        var teamIds = new HashSet<string>();
        foreach (var m in matches)
        {
            teamIds.Add(m.HomeTeamId);
            teamIds.Add(m.AwayTeamId);
        }

        // 2. Fetch all historical matches for these teams (Since 2020)
        // Optimization: We only need parsed matches with scores
        var startYear = 2020;
        var startDate = new DateTime(startYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        var historyMatches = await _context.Matches
            .Where(m => (teamIds.Contains(m.HomeTeamId) || teamIds.Contains(m.AwayTeamId)) 
                        && m.Date >= startDate 
                        && m.IsParsed 
                        && m.Score != null)
            .ToListAsync();

        // 3. Group by TeamId for O(1) lookup
        // A match belongs to Team X if X is Home OR Away
        var teamHistory = teamIds.ToDictionary(id => id, id => new List<Match>());

        foreach (var hMatch in historyMatches)
        {
            if (teamHistory.ContainsKey(hMatch.HomeTeamId)) teamHistory[hMatch.HomeTeamId].Add(hMatch);
            // A match acts as history for both teams involved
            if (teamHistory.ContainsKey(hMatch.AwayTeamId)) teamHistory[hMatch.AwayTeamId].Add(hMatch);
        }

        // 4. Analyze each match in memory
        foreach (var match in matches)
        {
            var homeParams = teamHistory.ContainsKey(match.HomeTeamId) ? teamHistory[match.HomeTeamId] : new List<Match>();
            var awayParams = teamHistory.ContainsKey(match.AwayTeamId) ? teamHistory[match.AwayTeamId] : new List<Match>();
            
            // H2H is intersection where ONE is Home and OTHER is Away (or vice versa)
            // But strict H2H means (Home=A & Away=B) OR (Home=B & Away=A)
            // We can filter from `homeParams` where opponent is `match.AwayTeamId`
            var h2hParams = homeParams.Where(m => m.HomeTeamId == match.AwayTeamId || m.AwayTeamId == match.AwayTeamId).ToList();

            result[match] = CalculateAnalysis(match, homeParams, awayParams, h2hParams);
        }

        return result;
    }

    private MatchAnalysisDto CalculateAnalysis(Match match, List<Match> homeAll, List<Match> awayAll, List<Match> h2hAll)
    {
        // Filter out the match itself if it's in history (unlikely if it's upcoming, but good for safety)
        // Actually, we want history *relative* to the match? usually yes, but if it's unseen upcoming, it won't be in history.
        // If it was a past match being re-analyzed, we should exclude it to avoid bias? 
        // For now, let's assume `homeAll` contains valid history.

        var stats = new List<YearlyStats>();
        var startYear = 2020;
        var currentYear = DateTime.UtcNow.Year + 1;

        // Yearly Stats
        for (int year = currentYear; year >= startYear; year--)
        {
            var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearEnd = yearStart.AddYears(1);

            var homeYear = homeAll.Where(m => m.Date >= yearStart && m.Date < yearEnd).ToList();
            if (homeYear.Any()) stats.Add(CalculateStatsInternal(match.HomeTeam, year, homeYear));

            var awayYear = awayAll.Where(m => m.Date >= yearStart && m.Date < yearEnd).ToList();
            if (awayYear.Any()) stats.Add(CalculateStatsInternal(match.AwayTeam, year, awayYear));

            var h2hYear = h2hAll.Where(m => m.Date >= yearStart && m.Date < yearEnd).ToList();
            if (h2hYear.Any()) stats.Add(CalculateStatsInternal("H2H Games", year, h2hYear));
        }

        // Prediction Logic (Weights, Bayesian, etc) -> Same as before but using the lists
        // 2. Count Total Games for base weights
        int countA = homeAll.Count;
        int countB = awayAll.Count;
        int countH = h2hAll.Count;

        // 3. Define Counters
        // Full Time
        int countA_O05 = homeAll.Count(m => m.GoalsCount > 0.5);
        int countA_O15 = homeAll.Count(m => m.GoalsCount > 1.5);
        int countA_O25 = homeAll.Count(m => m.GoalsCount > 2.5);

        int countB_O05 = awayAll.Count(m => m.GoalsCount > 0.5);
        int countB_O15 = awayAll.Count(m => m.GoalsCount > 1.5);
        int countB_O25 = awayAll.Count(m => m.GoalsCount > 2.5);

        int countH_O05 = h2hAll.Count(m => m.GoalsCount > 0.5);
        int countH_O15 = h2hAll.Count(m => m.GoalsCount > 1.5);
        int countH_O25 = h2hAll.Count(m => m.GoalsCount > 2.5);

        // First Half
        int countA_FH_O05 = homeAll.Count(m => (m.FirstHalfGoals ?? 0) > 0.5);
        int countA_FH_O15 = homeAll.Count(m => (m.FirstHalfGoals ?? 0) > 1.5);
        int countA_FH_O25 = homeAll.Count(m => (m.FirstHalfGoals ?? 0) > 2.5);

        int countB_FH_O05 = awayAll.Count(m => (m.FirstHalfGoals ?? 0) > 0.5);
        int countB_FH_O15 = awayAll.Count(m => (m.FirstHalfGoals ?? 0) > 1.5);
        int countB_FH_O25 = awayAll.Count(m => (m.FirstHalfGoals ?? 0) > 2.5);

        int countH_FH_O05 = h2hAll.Count(m => (m.FirstHalfGoals ?? 0) > 0.5);
        int countH_FH_O15 = h2hAll.Count(m => (m.FirstHalfGoals ?? 0) > 1.5);
        int countH_FH_O25 = h2hAll.Count(m => (m.FirstHalfGoals ?? 0) > 2.5);

        // Second Half (GoalsCount - FirstHalfGoals)
        int countA_SH_O05 = homeAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 0.5);
        int countA_SH_O15 = homeAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 1.5);
        int countA_SH_O25 = homeAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 2.5);

        int countB_SH_O05 = awayAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 0.5);
        int countB_SH_O15 = awayAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 1.5);
        int countB_SH_O25 = awayAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 2.5);

        int countH_SH_O05 = h2hAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 0.5);
        int countH_SH_O15 = h2hAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 1.5);
        int countH_SH_O25 = h2hAll.Count(m => (m.GoalsCount - (m.FirstHalfGoals ?? 0)) > 2.5);


        // 4. Calculate Smoothed Rates (Bayesian Avg)
        // Full Time
        double pA_05 = SmoothedRate(countA_O05, countA);
        double pB_05 = SmoothedRate(countB_O05, countB);
        double pH_05 = SmoothedRate(countH_O05, countH);

        double pA_15 = SmoothedRate(countA_O15, countA);
        double pB_15 = SmoothedRate(countB_O15, countB);
        double pH_15 = SmoothedRate(countH_O15, countH);

        double pA_25 = SmoothedRate(countA_O25, countA);
        double pB_25 = SmoothedRate(countB_O25, countB);
        double pH_25 = SmoothedRate(countH_O25, countH);

        // First Half
        double pA_FH_05 = SmoothedRate(countA_FH_O05, countA);
        double pB_FH_05 = SmoothedRate(countB_FH_O05, countB);
        double pH_FH_05 = SmoothedRate(countH_FH_O05, countH);

        double pA_FH_15 = SmoothedRate(countA_FH_O15, countA);
        double pB_FH_15 = SmoothedRate(countB_FH_O15, countB);
        double pH_FH_15 = SmoothedRate(countH_FH_O15, countH);

        double pA_FH_25 = SmoothedRate(countA_FH_O25, countA);
        double pB_FH_25 = SmoothedRate(countB_FH_O25, countB);
        double pH_FH_25 = SmoothedRate(countH_FH_O25, countH);

        // Second Half
        double pA_SH_05 = SmoothedRate(countA_SH_O05, countA);
        double pB_SH_05 = SmoothedRate(countB_SH_O05, countB);
        double pH_SH_05 = SmoothedRate(countH_SH_O05, countH);

        double pA_SH_15 = SmoothedRate(countA_SH_O15, countA);
        double pB_SH_15 = SmoothedRate(countB_SH_O15, countB);
        double pH_SH_15 = SmoothedRate(countH_SH_O15, countH);

        double pA_SH_25 = SmoothedRate(countA_SH_O25, countA);
        double pB_SH_25 = SmoothedRate(countB_SH_O25, countB);
        double pH_SH_25 = SmoothedRate(countH_SH_O25, countH);


        // 5. Calculate Weights (capped confidence)
        double wA = WeightFromSamples(countA, 40);
        double wB = WeightFromSamples(countB, 40);
        double wH = WeightFromSamples(countH, 10);

        // 6. Blend Probabilities
        double final_05 = Blend((pA_05, wA), (pB_05, wB), (pH_05, wH));
        double final_15 = Blend((pA_15, wA), (pB_15, wB), (pH_15, wH));
        double final_25 = Blend((pA_25, wA), (pB_25, wB), (pH_25, wH));

        double final_FH_05 = Blend((pA_FH_05, wA), (pB_FH_05, wB), (pH_FH_05, wH));
        double final_FH_15 = Blend((pA_FH_15, wA), (pB_FH_15, wB), (pH_FH_15, wH));
        double final_FH_25 = Blend((pA_FH_25, wA), (pB_FH_25, wB), (pH_FH_25, wH));

        double final_SH_05 = Blend((pA_SH_05, wA), (pB_SH_05, wB), (pH_SH_05, wH));
        double final_SH_15 = Blend((pA_SH_15, wA), (pB_SH_15, wB), (pH_SH_15, wH));
        double final_SH_25 = Blend((pA_SH_25, wA), (pB_SH_25, wB), (pH_SH_25, wH));

        var predictions = new PredictionStats
        {
            FullTime = $"≥0.5 = {ToPct(final_05)}, ≥1.5 = {ToPct(final_15)}, ≥2.5 = {ToPct(final_25)}",
            FirstHalf = $"≥0.5 = {ToPct(final_FH_05)}, ≥1.5 = {ToPct(final_FH_15)}, ≥2.5 = {ToPct(final_FH_25)}",
            SecondHalf = $"≥0.5 = {ToPct(final_SH_05)}, ≥1.5 = {ToPct(final_SH_15)}, ≥2.5 = {ToPct(final_SH_25)}"
        };

        return new MatchAnalysisDto
        {
            Stats = stats,
            Predictions = predictions
        };
    }

    private class StatResult : YearlyStats
    {
        public List<Match> _matches { get; set; } = new();
    }

    private StatResult CalculateStatsInternal(string teamName, int year, List<Match> matches)
    {
        var res = new StatResult { Year = year, Team = teamName, TotalGames = matches.Count, _matches = matches };
        
        foreach (var m in matches)
        {
            if (m.GoalsCount > 0.5) res.Over05++;
            if (m.GoalsCount > 1.5) res.Over15++;
            if (m.GoalsCount > 2.5) res.Over25++;

            var fh = m.FirstHalfGoals ?? 0;
            if (fh > 0.5) res.OverFH05++;
            if (fh > 1.5) res.OverFH15++;
            if (fh > 2.5) res.OverFH25++;
        }
        return res;
    }

    private string FormatPrediction(List<Match> matches, Func<Match, int?> goalSelector)
    {
        if (matches.Count == 0) return "No Data";
        
        int o05 = 0, o15 = 0, o25 = 0;
        foreach (var m in matches)
        {
            var g = goalSelector(m) ?? 0;
            if (g > 0.5) o05++;
            if (g > 1.5) o15++;
            if (g > 2.5) o25++;
        }

        double p05 = (double)o05 / matches.Count * 100;
        double p15 = (double)o15 / matches.Count * 100;
        double p25 = (double)o25 / matches.Count * 100;

        return $"≥0.5 = {p05:F0}%, ≥1.5 = {p15:F0}%, ≥2.5 = {p25:F0}%";
    }

    public bool MeetsConditions(MatchAnalysisDto analysis, List<FilterCondition> conditions)
    {
        foreach (var cond in conditions)
        {
            if (!cond.Enabled) continue;

            double probability = 0;
            var pred = analysis.Predictions;

            // Simplified mapping
            // PredictionStats stores strings like "≥0.5 = 93%, ..."
            
            if (cond.Half == "full")
            {
               if (cond.Threshold == "o05") probability = ParseValue(pred.FullTime, "≥0.5");
               if (cond.Threshold == "o15") probability = ParseValue(pred.FullTime, "≥1.5");
               if (cond.Threshold == "o25") probability = ParseValue(pred.FullTime, "≥2.5");
            }
            else if (cond.Half == "fh")
            {
               if (cond.Threshold == "o05") probability = ParseValue(pred.FirstHalf, "≥0.5");
               if (cond.Threshold == "o15") probability = ParseValue(pred.FirstHalf, "≥1.5");
               if (cond.Threshold == "o25") probability = ParseValue(pred.FirstHalf, "≥2.5");
            }
            else if (cond.Half == "sh")
            {
               if (cond.Threshold == "o05") probability = ParseValue(pred.SecondHalf, "≥0.5");
               if (cond.Threshold == "o15") probability = ParseValue(pred.SecondHalf, "≥1.5");
               if (cond.Threshold == "o25") probability = ParseValue(pred.SecondHalf, "≥2.5");
            }
            
            if (cond.Type == "under")
            {
                probability = 100 - probability;
            }

            if (probability < cond.MinPercent || probability > cond.MaxPercent)
            {
                return false;
            }
        }
        return true;
    }

    private double ParseValue(string text, string key)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var parts = text.Split(',');
        foreach (var part in parts)
        {
            var segments = part.Trim().Split('=');
            if (segments.Length == 2 && segments[0].Trim() == key)
            {
                if (double.TryParse(segments[1].Replace("%", "").Trim(), out var val))
                {
                    return val;
                }
            }
        }
        return 0;
    }
}
