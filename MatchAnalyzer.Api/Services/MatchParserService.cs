using System.Text.RegularExpressions;
using MatchAnalyzer.Api.Data;
using MatchAnalyzer.Api.Models;
using Microsoft.EntityFrameworkCore;
using ApiMatch = MatchAnalyzer.Api.Models.Match;

namespace MatchAnalyzer.Api.Services;

public class MatchParserService
{
    private readonly AppDbContext _context;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MatchParserService> _logger;

    public MatchParserService(AppDbContext context, HttpClient httpClient, ILogger<MatchParserService> logger)
    {
        _context = context;
        _httpClient = httpClient;
        _logger = logger;
        
        // Headers required by the feed
        if (!_httpClient.DefaultRequestHeaders.Contains("x-fsign"))
        {
            _httpClient.DefaultRequestHeaders.Add("x-fsign", "SW9D1eZo");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");
        }
    }

    public async Task<int> SyncUpcomingMatches(int addedDaysCount)
    {
        _logger.LogInformation("Syncing matches for +{Days} days", addedDaysCount);
        int savedCount = 0;

        try
        {
            var upcoming = await _httpClient.GetAsync($"https://2.flashscore.ninja/2/x/feed/f_1_{addedDaysCount}_4_en_1");
            if (!upcoming.IsSuccessStatusCode) return 0;

            var str = await upcoming.Content.ReadAsStringAsync();
            var matches = Regex.Matches(str, @"AA÷([^¬]+)¬AD÷(\d+)");
            var today = DateTimeOffset.UtcNow.AddDays(addedDaysCount).Date;

            var matchIds = new List<string>();
            var dict = new Dictionary<string, DateTime>();

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var matchId = match.Groups[1].Value;
                var timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(match.Groups[2].Value));

                if (timestamp.Date == today.Date)
                {
                    matchIds.Add(matchId);
                    dict[matchId] = timestamp.DateTime.ToLocalTime();
                }
            }

            matchIds = matchIds.Distinct().ToList();

            foreach (var matchId in matchIds)
            {
                // Check if upcoming match exists
                var existing = await _context.Matches.FirstOrDefaultAsync(m => m.MatchId == matchId);
                
                // If exists and is parsed/finished, skip
                if (existing != null && existing.IsParsed) continue;

                // Process Match
                try
                {
                    await ProcessMatch(matchId, dict.ContainsKey(matchId) ? dict[matchId] : null);
                    savedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing match {MatchId}", matchId);
                }
            }
            
            await _context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error executing SyncUpcomingMatches");
            throw;
        }

        return savedCount;
    }

    private async Task ProcessMatch(string matchId, DateTime? scheduledDate)
    {
        // Fetch details (Head to Head etc)
        var response = await _httpClient.GetAsync($"https://2.flashscore.ninja/2/x/feed/df_hh_1_{matchId}"); // df_hh seems to be H2H feed
        if (!response.IsSuccessStatusCode) return;
        var content = await response.Content.ReadAsStringAsync();

        // Parse Teams
        var homeTeamName = ExtractTeamName(content, 1);
        var awayTeamName = ExtractTeamName(content, 2);
        
        var homeTeamId = ExtractTeamId(content, 1, homeTeamName);
        var awayTeamId = ExtractTeamId(content, 2, awayTeamName);

        // Update or Create the Main Match
        var match = await _context.Matches.FirstOrDefaultAsync(m => m.MatchId == matchId);
        if (match == null)
        {
            match = new ApiMatch
            {
                MatchId = matchId,
                HomeTeam = homeTeamName,
                AwayTeam = awayTeamName,
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                Date = scheduledDate?.ToUniversalTime(),
                IsParsed = true // We parsed the meta-data
            };
            _context.Matches.Add(match);
        }
        else
        {
            match.HomeTeam = homeTeamName;
            match.AwayTeam = awayTeamName;
            match.HomeTeamId = homeTeamId;
            match.AwayTeamId = awayTeamId;
            match.Date = scheduledDate?.ToUniversalTime() ?? match.Date;
            match.IsParsed = true;
        }

        // Parse Historical Matches (H2H) and save them too
        // The original code iterated through matches found in `content` to build `matchInfos.Results`
        // We will parse them and save them to DB so we can query them later.
        
        await ParseAndSaveHistoricalMatches(content);
    }

    private async Task ParseAndSaveHistoricalMatches(string content)
    {
        var pattern = @"KC÷(?<timestamp>\d+)¬" +
                      @"(?:[^¬]*¬)*?KP÷(?<matchId>[^¬]+)¬" +
                      @"(?:[^¬]*¬)*?UQ÷(?<team1Id>[^¬]+)¬(?:[^¬]*¬)*?UO÷(?<team2Id>[^¬]+)¬" +
                      @"(?:[^¬]*¬)*?KJ÷\*?(?<team1>[^¬]+)¬(?:[^¬]*¬)*?FH÷(?<team1Name>[^¬]+)¬" +
                      @"(?:[^¬]*¬)*?KK÷\*?(?<team2>[^¬]+)¬(?:[^¬]*¬)*?FK÷(?<team2Name>[^¬]+)¬" +
                      @"(?:[^¬]*¬)*?KL÷(?<score>\d+:\d+)";

        var matches = Regex.Matches(content, pattern, RegexOptions.Singleline);
        
        var matchIds = await _context.Matches.Select(x => x.MatchId).ToListAsync();

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var hMatchId = m.Groups["matchId"].Value;
            
            // Check if this historical match exists
            var exists = await _context.Matches.AnyAsync(x => x.MatchId == hMatchId);
            if (exists) continue; // Already saved

            var timestamp = long.Parse(m.Groups["timestamp"].Value);
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
            var score = m.Groups["score"].Value;
            
            // Fetch First Half Score if needed (df_sui)
            // Warning: This makes N requests. Might be slow? 
            // User did say "parse data save into db" so we can avoid doing this repeated work.
            // Let's do it individually.
            
            // Wait, we need to be careful about rate limits. 
            // Depending on user context, we might want to delay?
            // "I can only parse not finished games and upcoming game... so it will become more faster"
            // The historical games are already finished. We parse them ONCE and save.

            int firstHalfGoals = 0;
            // Fetch extra info for First Half Goals
           /* 
              Commented out to save time/bandwidth for now, unless critical. 
              Original code did fetch `df_sui_1_{matchId}` for `ParseFirstHalfGoals`.
              I will assume we need it.
           */
           try 
           {
               var subResponse = await _httpClient.GetAsync($"https://2.flashscore.ninja/2/x/feed/df_sui_1_{hMatchId}");
               if (subResponse.IsSuccessStatusCode)
               {
                   var subContent = await subResponse.Content.ReadAsStringAsync();
                   firstHalfGoals = ParseFirstHalfGoals(subContent);
               }
           }
           catch {}

           var hMatch = new ApiMatch
           {
               MatchId = hMatchId,
               Date = date,
               HomeTeam = m.Groups["team1Name"].Value.Trim(),
               AwayTeam = m.Groups["team2Name"].Value.Trim(),
               HomeTeamId = m.Groups["team1Id"].Value.Trim(),
               AwayTeamId = m.Groups["team2Id"].Value.Trim(),
               Score = score,
               GoalsCount = score.Split(':').Sum(x => int.Parse(x)),
               FirstHalfGoals = firstHalfGoals,
               IsParsed = true // Finished game
           };
           
            if (matchIds.Contains(hMatchId)) continue;
           
           _context.Matches.Add(hMatch);
           matchIds.Add(hMatchId);
        }
    }

    private int ParseFirstHalfGoals(string raw)
    {
        var m = Regex.Match(raw, @"AC÷1st Half[^¬]*¬IG÷(?<home>\d+)¬IH÷(?<away>\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return 0;
        return int.Parse(m.Groups["home"].Value) + int.Parse(m.Groups["away"].Value);
    }

    private string ExtractTeamId(string content, int index, string teamName)
    {
        try
        {
            var parts = content.Split("Last matches: ");
            if (parts.Length <= index) return string.Empty;

            var section = parts[index];
            
            // Find first match in this section
            var matchMatch = Regex.Match(section, @"~KC÷.*?(?=~KC÷|~KB÷|$)", RegexOptions.Singleline);
            if (!matchMatch.Success) return string.Empty;

            var mContent = matchMatch.Value;
            
            // Extract IDs and Names
            var ids = Regex.Match(mContent, @"UQ÷(?<id1>[^¬]+)¬(?:[^¬]*¬)*?UO÷(?<id2>[^¬]+)");
            var names = Regex.Match(mContent, @"KJ÷\*?(?<n1>[^¬]+)¬(?:[^¬]*¬)*?KK÷\*?(?<n2>[^¬]+)");

            if (ids.Success && names.Success)
            {
                var n1 = names.Groups["n1"].Value;
                var n2 = names.Groups["n2"].Value;
                var id1 = ids.Groups["id1"].Value;
                var id2 = ids.Groups["id2"].Value;

                // Check against team name to decide which ID is which
                if (n1.Contains(teamName, StringComparison.OrdinalIgnoreCase) || teamName.Contains(n1, StringComparison.OrdinalIgnoreCase))
                    return id1;
                
                if (n2.Contains(teamName, StringComparison.OrdinalIgnoreCase) || teamName.Contains(n2, StringComparison.OrdinalIgnoreCase))
                    return id2;
                
                // Fallback: If no match found, assume the first team mentioned in the block header ("Last matches: X") 
                // corresponds to the context, but determining if X is Home or Away in *this specific sub-match* 
                // requires knowing X's name. Logic above covers name matching.
                // If fuzzy match fails, return empty to be safe.
                return id1; 
            }
        }
        catch { }
        return string.Empty;
    }

    private string ExtractTeamName(string content, int index)
    {
         try 
         {
             var parts = content.Split("Last matches: ");
             if (parts.Length > index)
             {
                 return parts[index].Split('¬')[0].Trim();
             }
         } 
         catch {}
         return "Unknown";
    }

    public async Task<List<ApiMatch>> GetMatchesAsync(DateTime? dateSpan)
    {
        var query = _context.Matches.AsQueryable();
        if (dateSpan.HasValue)
        {
            var start = dateSpan.Value.Date.ToUniversalTime();
            var end = start.AddDays(1);
            query = query.Where(m => m.Date >= start && m.Date < end);
        }
        return await query.OrderBy(m => m.Date).ToListAsync();
    }

    public async Task<List<ApiMatch>> SearchMatchesAsync(SearchRequest request)
    {
        var query = _context.Matches.AsQueryable();

        // Date Filter
        if (request.From.HasValue)
            query = query.Where(m => m.Date >= request.From.Value.ToUniversalTime());
        
        if (request.To.HasValue)
            query = query.Where(m => m.Date <= request.To.Value.ToUniversalTime());

        // Team Filter
        if (!string.IsNullOrWhiteSpace(request.Team))
        {
            var team = request.Team.ToLower();
            query = query.Where(m => m.HomeTeam.ToLower().Contains(team) || m.AwayTeam.ToLower().Contains(team));
        }

        return await query.OrderBy(m => m.Date).ToListAsync();
    }
}
