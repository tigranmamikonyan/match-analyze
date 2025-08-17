using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TetsConsole.Db;
using Match = System.Text.RegularExpressions.Match;

await UpdatePendingMatches();

var client = new HttpClient();

client.DefaultRequestHeaders.Add("x-fsign", "SW9D1eZo");
client.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36");

var addedDaysCount = 7;

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

var mustBeMatches = new List<UpcomingMatch>();

foreach (var match in matchInfos)
{
    var currentYearMatches = match.Results.Where(x => x.Date.Year == DateTime.UtcNow.Year).ToList();

    if (currentYearMatches.Count > 10)
    {
        if (currentYearMatches.Count(x => x.GoalsCount > 1.5) >= currentYearMatches.Count - 1 &&
            currentYearMatches.Count(x => x.GoalsCount > 0.5) == currentYearMatches.Count)
        {
            mustBeMatches.Add(match);
        }
    }
}

await SavetMustBeMatches(mustBeMatches, dict);

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
            var homeTeamOver05 = filteredResult
                .Where(x => x.GoalsCount >= 0.5)
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            var homeTeamOver15 = filteredResult
                .Where(x => x.GoalsCount >= 1.5)
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            var homeTeamOver25 = filteredResult
                .Where(x => x.GoalsCount >= 2.5)
                .Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);

            var awayTotalGames = filteredResult
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            var awayTeamOver05 = filteredResult
                .Where(x => x.GoalsCount >= 0.5)
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            var awayTeamOver15 = filteredResult
                .Where(x => x.GoalsCount >= 1.5)
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            var awayTeamOver25 = filteredResult
                .Where(x => x.GoalsCount >= 2.5)
                .Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);

            var h2hTotalGames = filteredResult
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
            var h2hOver05 = filteredResult
                .Where(x => x.GoalsCount >= 0.5)
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
            var h2hOver15 = filteredResult
                .Where(x => x.GoalsCount >= 1.5)
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));
            var h2hOver25 = filteredResult
                .Where(x => x.GoalsCount >= 2.5)
                .Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));

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
    }

    html = html.Replace("{match_section}", resultMatchSection);

    File.WriteAllText($"{date.Date.ToString("yyyy-MM-dd-")}index.html", html);

    Console.WriteLine("Done");
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