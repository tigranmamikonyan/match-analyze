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

var addedDaysCount = 1;

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

    h2 {
      margin-top: 0;
    }

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

    th {
      background: #eee;
    }

    .subheader {
      background: #ddd;
      font-weight: bold;
    }

    .h2h-table {
      margin-top: 20px;
    }
  </style>
</head>
<body>

  <h1>Football Match Statistics (Last 5 Years)</h1>
  {match_section}
</body>
</html>";

    var resultMatchSection = "";

    foreach (var match in matches)
    {
        if (match.Results.Count == 0)
        {
            continue;
        }

        var data = @"<tr>
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
        <tr>  <!-- ADDED -->
          <td>{year}</td>
          <td>H2H Games</td>
          <td>{h2hTotalGames}</td>
          <td>{h2hOver0.5}</td>
          <td>{h2hOver1.5}</td>
          <td>{h2hOver2.5}</td>
        </tr>";


        var matchSection = @"<div class=""match-section"">
    <a href='https://www.flashscore.com/match/football/{matchId}/#/match-summary/match-summary'> {homeTeamName} vs {awayTeamName}</a>

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
  </div>";

        matchSection = matchSection.Replace("{homeTeamName}", match.HomeTeam)
            .Replace("{awayTeamName}",
                match.AwayTeam + " (" + match.MatchId + ")" + $"{dict[match.MatchId].ToString("yyyy-MM-dd HH:mm")}")
            .Replace("{matchId}", match.MatchId);
        ;

        var filteredResults = match.Results.Where(x => x.Date.Year >= 2020)
            .GroupBy(x => x.Date.Year)
            .ToList();

        var resultData = "";
        foreach (var filteredResult in filteredResults)
        {
            var yearData = data;

            yearData = yearData.Replace("{year}", filteredResult.Key.ToString());

            yearData = yearData.Replace("{homeTeamName}", match.HomeTeam);
            yearData = yearData.Replace("{awayTeamName}", match.AwayTeam);

            var homeTotalGames = filteredResult
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            // FIX thresholds: use integers 1/2/3
            var homeTeamOver05 = filteredResult
                .Where(x => x.GoalsCount >= 1) // was 0.5
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            var homeTeamOver15 = filteredResult
                .Where(x => x.GoalsCount >= 2) // was 1.5
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            var homeTeamOver25 = filteredResult
                .Where(x => x.GoalsCount >= 3) // was 2.5
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);

            var awayTotalGames = filteredResult
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            var awayTeamOver05 = filteredResult
                .Where(x => x.GoalsCount >= 1)
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            var awayTeamOver15 = filteredResult
                .Where(x => x.GoalsCount >= 2)
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            var awayTeamOver25 = filteredResult
                .Where(x => x.GoalsCount >= 3)
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);

            var h2hTotalGames = filteredResult
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
            var h2hOver05 = filteredResult
                .Where(x => x.GoalsCount >= 1)
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
            var h2hOver15 = filteredResult
                .Where(x => x.GoalsCount >= 2)
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
            var h2hOver25 = filteredResult
                .Where(x => x.GoalsCount >= 3)
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.HomeTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));

// --- Smoothed Over0.5 (≥1 goal) rates
            double pA = SmoothedRate(homeTeamOver05, homeTotalGames); // home team history
            double pB = SmoothedRate(awayTeamOver05, awayTotalGames); // away team history
            double pH = SmoothedRate(h2hOver05, h2hTotalGames); // head-to-head

// --- Weights by sample sizes (caps prevent tiny samples dominating)
// e.g., team form: cap at 40 games; H2H: cap at 10 games
            double wA = WeightFromSamples(homeTotalGames, 40);
            double wB = WeightFromSamples(awayTotalGames, 40);
            double wH = WeightFromSamples(h2hTotalGames, 10);

// --- Final probability as weighted blend
            double pAnyGoal = Blend((pA, wA), (pB, wB), (pH, wH));


            yearData = yearData.Replace("{homeTotalGames}", homeTotalGames.ToString());
            yearData = yearData.Replace("{homeOver0.5}", homeTeamOver05.ToString());
            yearData = yearData.Replace("{homeOver1.5}", homeTeamOver15.ToString());
            yearData = yearData.Replace("{homeOver2.5}", homeTeamOver25.ToString());
            yearData = yearData.Replace("{awayTotalGames}", awayTotalGames.ToString());
            yearData = yearData.Replace("{awayOver0.5}", awayTeamOver05.ToString());
            yearData = yearData.Replace("{awayOver1.5}", awayTeamOver15.ToString());
            yearData = yearData.Replace("{awayOver2.5}", awayTeamOver25.ToString());
            yearData = yearData.Replace("{h2hTotalGames}", h2hTotalGames.ToString());
            yearData = yearData.Replace("{h2hOver0.5}", h2hOver05.ToString());
            yearData = yearData.Replace("{h2hOver1.5}", h2hOver15.ToString());
            yearData = yearData.Replace("{h2hOver2.5}", h2hOver25.ToString());

            resultData += yearData;
        }

        resultMatchSection += matchSection.Replace("{resultData}", resultData);
        
        // After the year loop, recompute on all filtered rows (since 2020)
        var allRows = match.Results.Where(x => x.Date.Year >= 2020).ToList();

        int A_total = allRows.Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
        int A_over  = allRows.Count(x => x.GoalsCount >= 1 && 
                                         (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));

        int B_total = allRows.Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
        int B_over  = allRows.Count(x => x.GoalsCount >= 1 && 
                                         (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));

        int H_total = allRows.Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                         (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
        int H_over  = allRows.Count(x => x.GoalsCount >= 1 &&
                                         ((x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                          (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        double pA_all = SmoothedRate(A_over, A_total);
        double pB_all = SmoothedRate(B_over, B_total);
        double pH_all = SmoothedRate(H_over, H_total);

        double wA_all = WeightFromSamples(A_total, 40);
        double wB_all = WeightFromSamples(B_total, 40);
        double wH_all = WeightFromSamples(H_total, 10);

        double pAny_all = Blend((pA_all, wA_all), (pB_all, wB_all), (pH_all, wH_all));

// append a small note under the table
                resultMatchSection += $@"<div style=""margin-top:8px;font-size:14px;color:#333"">
          <strong>Prediction:</strong> ≥1 goal = {ToPct(pAny_all)} 
          <span style=""opacity:0.7"">(A:{ToPct(pA_all)}, B:{ToPct(pB_all)}{(H_total>0?$", H2H:{ToPct(pH_all)}":"")})</span>
        </div>";

    }

    html = html.Replace("{match_section}", resultMatchSection);

    File.WriteAllText($"{date.Date.ToString("yyyy-MM-dd-")}index.html", html);

    Console.WriteLine("Done");
}

static double SmoothedRate(int success, int total, double alpha = 1.0, double beta = 1.0)
    => total <= 0 ? 0.5 : (success + alpha) / (total + alpha + beta);

// turn a sample size into a weight (cap so H2H doesn’t dominate)
static double WeightFromSamples(int n, int cap) => Math.Min(n, cap) / (double)cap;

// combine pA, pB, pH2H via weighted average (simple, stable)
static double Blend(params (double p, double w)[] terms)
{
    double sw = terms.Sum(t => t.w);
    if (sw <= 0) return 0.5;
    return terms.Sum(t => t.p * t.w) / sw;
}

// optional: pretty percent
static string ToPct(double p) => $"{Math.Round(p * 100)}%";


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