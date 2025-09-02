using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TetsConsole.Db;
using Match = System.Text.RegularExpressions.Match;

// await UpdatePendingMatches();

var client = new HttpClient();

client.DefaultRequestHeaders.Add("x-fsign", "SW9D1eZo");
client.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");

var addedDaysCount = 0;

var upcoming = await client.GetAsync($"https://2.flashscore.ninja/2/x/feed/f_1_{addedDaysCount}_4_en_1");
var str = await upcoming.Content.ReadAsStringAsync();

var matches = Regex.Matches(str, @"AA÷([^¬]+)¬AD÷(\d+)");
var today = DateTimeOffset.UtcNow.AddDays(addedDaysCount).Date;

var matchIds = new List<string>();
var dict = new Dictionary<string, DateTime>();

foreach (Match match in matches)
{
    var matchId = match.Groups[1].Value;
    var timestamp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(match.Groups[2].Value));

    if (timestamp.Date == today.Date)
    {
        Console.WriteLine(matchId);
        matchIds.Add(matchId);
        dict[matchId] = timestamp.DateTime.ToLocalTime();
    }
}

matchIds = matchIds.Distinct().ToList();

var matchInfos = new List<UpcomingMatch>();

foreach (var matchId in matchIds)
{
    var response = await client.GetAsync($"https://2.flashscore.ninja/2/x/feed/df_hh_1_{matchId}");
    var content = await response.Content.ReadAsStringAsync();

    var homeTeamName = content.Split("Last matches: ")[1].Split('¬')[0];
    var awayTeamName = content.Split("Last matches: ")[2].Split('¬')[0];

    var results = GetMatchInfo(content);

    matchInfos.Add(new UpcomingMatch
    {
        HomeTeam = homeTeamName.Trim(),
        AwayTeam = awayTeamName.Trim(),
        MatchId = matchId,
        Results = results
    });
}

// var mustBeMatches = new List<UpcomingMatch>();
//  
// foreach (var match in matchInfos)
// {
//     var currentYearMatches = match.Results.Where(x => x.Date.Year == DateTime.UtcNow.Year).ToList();
//
//     if (currentYearMatches.Count > 10)
//     {
//         if (currentYearMatches.Count(x => x.GoalsCount > 1.5) >= currentYearMatches.Count - 1 &&
//             currentYearMatches.Count(x => x.GoalsCount > 0.5) == currentYearMatches.Count)
//         {
//             mustBeMatches.Add(match);
//         }
//     }
// }
//
// await SavetMustBeMatches(mustBeMatches, dict);

dict = dict.OrderBy(x => x.Value)
    .ToDictionary(x => x.Key, x => x.Value);

matchInfos = matchInfos.OrderBy(x => dict).ToList();

matchInfos = matchInfos
    .OrderBy(mi => dict[mi.MatchId])
    .ToList();


MakeHtml(matchInfos, dict, today);

Console.WriteLine();

static async Task SavetMustBeMatches(List<UpcomingMatch> matches, Dictionary<string, DateTime> dateTimes)
{
    using var db = new AppDbContext();

    var existingMatchIds = await db.Matches.Select(x => x.MatchId).ToListAsync();

    foreach (var match in matches)
    {
        if (existingMatchIds.Contains(match.MatchId))
        {
            continue;
        }

        db.Matches.Add(new TetsConsole.Db.Match
        {
            AwayTeam = match.AwayTeam,
            HomeTeam = match.HomeTeam,
            IsParsed = false,
            MatchId = match.MatchId,
            Date = dateTimes[match.MatchId].ToUniversalTime()
        });
    }

    await db.SaveChangesAsync();
}

static async Task UpdatePendingMatches()
{
    using var client = new HttpClient();

    client.DefaultRequestHeaders.Add("x-fsign", "SW9D1eZo");
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");

    using var db = new AppDbContext();
    var matches = await db.Matches.Where(x => x.IsParsed == false && x.Date < DateTime.UtcNow.AddHours(-5))
        .ToListAsync();

    foreach (var match in matches)
    {
        var response =
            await client.GetAsync($"https://www.flashscore.com/match/football/{match.MatchId}/#/match-summary");
        var content = await response.Content.ReadAsStringAsync();

        var data = content.Split("og:title").LastOrDefault().Split(">").FirstOrDefault().Split(" ").LastOrDefault();

        if (data.Contains(":"))
        {
            data = data.Replace("\"", "").Trim();

            var score = data;

            var goalsCount = score.Split(':').Sum(x => int.Parse(x));

            match.Score = score;
            match.GoalsCount = goalsCount;
            match.IsParsed = true;
            await db.SaveChangesAsync();
        }
        else
        {
            if (match.Date < DateTime.UtcNow.AddDays(-2))
            {
                match.IsParsed = true;
                await db.SaveChangesAsync();
            }
        }
    }
}

static List<MatchResult> GetMatchInfo(string input)
{
    var pattern =
        @"KC÷(?<timestamp>\d+).*?KJ÷\*?(?<team1>[^¬]+)¬FH÷(?<team1Name>[^¬]+).*?KK÷\*?(?<team2>[^¬]+)¬FK÷(?<team2Name>[^¬]+).*?KL÷(?<score>\d+:\d+)";
    var matches = Regex.Matches(input, pattern, RegexOptions.Singleline);

    var results = new List<MatchResult>();

    foreach (Match match in matches)
    {
        var timestamp = long.Parse(match.Groups["timestamp"].Value);
        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm");

        results.Add(new MatchResult
        {
            Date = DateTime.Parse(date),
            HomeTeam = match.Groups["team1Name"].Value.Trim(),
            AwayTeam = match.Groups["team2Name"].Value.Trim(),
            Score = match.Groups["score"].Value,
            GoalsCount = match.Groups["score"].Value.Split(':').Sum(x => int.Parse(x))
        });
    }

    results = results.GroupBy(x => x.Date).Select(x => x.First()).ToList();

    // Print the results
    foreach (var result in results)
    {
        Console.WriteLine($"{result.Date}: {result.HomeTeam} vs {result.AwayTeam} => {result.Score}");
    }

    return results;
}

// ----- Helpers (put inside the same class) -----
static double SmoothedRate(int success, int total, double alpha = 1.0, double beta = 1.0)
    => total <= 0 ? 0.5 : (success + alpha) / (total + alpha + beta);

static double WeightFromSamples(int n, int cap) => Math.Min(Math.Max(n, 0), cap) / (double)cap;

static double Blend(params (double p, double w)[] terms)
{
    double sw = terms.Sum(t => t.w);
    if (sw <= 0) return 0.5;
    return terms.Sum(t => t.p * t.w) / sw;
}

static string ToPct(double p) => $"{Math.Round(p * 100)}%";

// ----- Main function -----
static void MakeHtml(List<UpcomingMatch> matches, Dictionary<string, DateTime> dict, DateTime date)
{
    var html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <title>Football Match Stats</title>
  <style>
    body {
      font-family: Arial, sans-serif;
      background: #f4f4f4;
      padding: 30px;
    }
    .match-section {
      background: #fff;
      margin-bottom: 40px;
      padding: 20px;
      border-radius: 12px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    }
    h2 { margin-top: 0; }
    table {
      width: 100%;
      border-collapse: collapse;
      margin-top: 15px;
    }
    th, td {
      border: 1px solid #ccc;
      text-align: center;
      padding: 8px;
      font-size: 14px;
    }
    th { background: #eee; }
    .pred {
      margin-top:8px;
      font-size:14px;
      color:#333;
    }
    .pred small { opacity:0.7; }
  </style>
</head>
<body>
  <h1>Football Match Statistics (Last 5 Years)</h1>
  {match_section}
</body>
</html>";

    var resultMatchSection = "";
    var resultMatchSectionOver90 = "";

    foreach (var match in matches)
    {
        if (match?.Results == null || match.Results.Count == 0)
            continue;

        // row template (includes proper <tr> for H2H)
        var rowTpl = @"<tr>
  <td>{year}</td>
  <td>{homeTeamName}</td>
  <td>{homeTotalGames}</td>
  <td>{homeOver0.5}</td>
  <td>{homeOver1.5}</td>
  <td>{homeOver2.5}</td>
</tr>
<tr>
  <td>{year}</td>
  <td>{awayTeamName}</td>
  <td>{awayTotalGames}</td>
  <td>{awayOver0.5}</td>
  <td>{awayOver1.5}</td>
  <td>{awayOver2.5}</td>
</tr>
<tr>
  <td>{year}</td>
  <td>H2H Games</td>
  <td>{h2hTotalGames}</td>
  <td>{h2hOver0.5}</td>
  <td>{h2hOver1.5}</td>
  <td>{h2hOver2.5}</td>
</tr>";

        var matchSectionTpl = @"<div class=""match-section"">
  <a href='https://www.flashscore.com/match/football/{matchId}/#/match-summary/match-summary'>
    {homeTeamName} vs {awayTeamName}
  </a>
  <div><small>{kickoff}</small></div>

  <table>
    <tr>
      <th>Year</th>
      <th>Team</th>
      <th>Total Games</th>
      <th>Over 0.5</th>
      <th>Over 1.5</th>
      <th>Over 2.5</th>
    </tr>
    {resultData}
  </table>
  {overallPredictionBlock}
</div>";

        // Compose header text safely
        dict.TryGetValue(match.MatchId, out var ko);
        var kickoffTxt = ko == default ? "" : ko.ToString("yyyy-MM-dd HH:mm");

        var matchSection = matchSectionTpl
            .Replace("{homeTeamName}", match.HomeTeam)
            .Replace("{awayTeamName}", $"{match.AwayTeam} ({match.MatchId})")
            .Replace("{matchId}", match.MatchId)
            .Replace("{kickoff}", kickoffTxt);

        // Yearly breakdown (2020+)
        var filteredByYear = match.Results
            .Where(x => x.Date.Year >= 2020)
            .GroupBy(x => x.Date.Year)
            .OrderByDescending(g => g.Key)
            .ToList();

        var resultData = "";
        foreach (var yrGroup in filteredByYear)
        {
            var yearData = rowTpl.Replace("{year}", yrGroup.Key.ToString())
                                 .Replace("{homeTeamName}", match.HomeTeam)
                                 .Replace("{awayTeamName}", match.AwayTeam);

            // totals (per side / per year)
            int homeTotal = yrGroup.Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            int awayTotal = yrGroup.Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            int h2hTotal  = yrGroup.Count(x =>
                               (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                               (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));

            // counts with proper integer thresholds
            int homeO05 = yrGroup.Count(x => x.GoalsCount >= 1 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
            int homeO15 = yrGroup.Count(x => x.GoalsCount >= 2 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
            int homeO25 = yrGroup.Count(x => x.GoalsCount >= 3 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));

            int awayO05 = yrGroup.Count(x => x.GoalsCount >= 1 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
            int awayO15 = yrGroup.Count(x => x.GoalsCount >= 2 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
            int awayO25 = yrGroup.Count(x => x.GoalsCount >= 3 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));

            int h2hO05 = yrGroup.Count(x => x.GoalsCount >= 1 && (
                                (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));
            int h2hO15 = yrGroup.Count(x => x.GoalsCount >= 2 && (
                                (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));
            int h2hO25 = yrGroup.Count(x => x.GoalsCount >= 3 && (
                                (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

            // fill table counts
            yearData = yearData.Replace("{homeTotalGames}", homeTotal.ToString())
                               .Replace("{homeOver0.5}", homeO05.ToString())
                               .Replace("{homeOver1.5}", homeO15.ToString())
                               .Replace("{homeOver2.5}", homeO25.ToString())
                               .Replace("{awayTotalGames}", awayTotal.ToString())
                               .Replace("{awayOver0.5}", awayO05.ToString())
                               .Replace("{awayOver1.5}", awayO15.ToString())
                               .Replace("{awayOver2.5}", awayO25.ToString())
                               .Replace("{h2hTotalGames}", h2hTotal.ToString())
                               .Replace("{h2hOver0.5}", h2hO05.ToString())
                               .Replace("{h2hOver1.5}", h2hO15.ToString())
                               .Replace("{h2hOver2.5}", h2hO25.ToString());

            resultData += yearData;
        }

        // Insert yearly table rows
        matchSection = matchSection.Replace("{resultData}", resultData);

        // --- Overall (since 2020) blended predictions for ≥0.5 / ≥1.5 / ≥2.5 ---
        var allRows = match.Results.Where(x => x.Date.Year >= 2020).ToList();

        int A_total = allRows.Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
        int B_total = allRows.Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
        int H_total = allRows.Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                         (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));

        // ≥1 goal
        int A_o05 = allRows.Count(x => x.GoalsCount >= 1 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o05 = allRows.Count(x => x.GoalsCount >= 1 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o05 = allRows.Count(x => x.GoalsCount >= 1 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        // ≥2 goals
        int A_o15 = allRows.Count(x => x.GoalsCount >= 2 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o15 = allRows.Count(x => x.GoalsCount >= 2 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o15 = allRows.Count(x => x.GoalsCount >= 2 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        // ≥3 goals
        int A_o25 = allRows.Count(x => x.GoalsCount >= 3 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o25 = allRows.Count(x => x.GoalsCount >= 3 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o25 = allRows.Count(x => x.GoalsCount >= 3 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        // smoothed rates
        double pA05 = SmoothedRate(A_o05, A_total), pB05 = SmoothedRate(B_o05, B_total), pH05 = SmoothedRate(H_o05, H_total);
        double pA15 = SmoothedRate(A_o15, A_total), pB15 = SmoothedRate(B_o15, B_total), pH15 = SmoothedRate(H_o15, H_total);
        double pA25 = SmoothedRate(A_o25, A_total), pB25 = SmoothedRate(B_o25, B_total), pH25 = SmoothedRate(H_o25, H_total);

        // weights
        double wA = WeightFromSamples(A_total, 40);
        double wB = WeightFromSamples(B_total, 40);
        double wH = WeightFromSamples(H_total, 10);

        // blended probs
        double pOver05_all = Blend((pA05, wA), (pB05, wB), (pH05, wH));
        double pOver15_all = Blend((pA15, wA), (pB15, wB), (pH15, wH));
        double pOver25_all = Blend((pA25, wA), (pB25, wB), (pH25, wH));

        var overallBlock = $@"<div class=""pred"">
  <strong>Predictions (since 2020):</strong>
  ≥0.5 = {ToPct(pOver05_all)}, ≥1.5 = {ToPct(pOver15_all)}, ≥2.5 = {ToPct(pOver25_all)}
  <br/><small>
    A≥0.5:{ToPct(pA05)}, B≥0.5:{ToPct(pB05)}{(H_total>0 ? $", H2H≥0.5:{ToPct(pH05)}" : "")}
  </small>
</div>";

        matchSection = matchSection.Replace("{overallPredictionBlock}", overallBlock);
        
        if (pOver05_all > 0.90)
        {
            resultMatchSectionOver90 += matchSection;
        }

        resultMatchSection += matchSection;
    }


    var outPath = $"{date.Date:yyyy-MM-dd-}index.html";
    var outPathOver90 = $"{date.Date:yyyy-MM-dd-}index-only-over.html";
    File.WriteAllText(outPath, html.Replace("{match_section}", resultMatchSection));
    File.WriteAllText(outPathOver90, html.Replace("{match_section}", resultMatchSectionOver90));
    Console.WriteLine($"Done -> {outPath}");
}


public class MatchResult
{
    public DateTime Date { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string Score { get; set; }
    public int GoalsCount { get; set; }
}

public class UpcomingMatch
{
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string MatchId { get; set; }
    public List<MatchResult> Results { get; set; }
}