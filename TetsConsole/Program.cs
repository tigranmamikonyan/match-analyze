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
    try
    {
        var response = await client.GetAsync($"https://2.flashscore.ninja/2/x/feed/df_hh_1_{matchId}");
        var content = await response.Content.ReadAsStringAsync();

        var homeTeamName = content.Split("Last matches: ")[1].Split('¬')[0];
        var awayTeamName = content.Split("Last matches: ")[2].Split('¬')[0];

        var results = await GetMatchInfo(content, client);
        
        matchInfos.Add(new UpcomingMatch
        {
            HomeTeam = homeTeamName.Trim(),
            AwayTeam = awayTeamName.Trim(),
            MatchId = matchId,
            Results = results
        });
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
        await Task.Delay(1000 * 60);
        continue;
    }
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

static int ParseFirstHalfGoals(string raw)
{
    // Looks for the "1st Half" block and pulls IG (home) and IH (away)
    var m = Regex.Match(
        raw,
        @"AC÷1st Half[^¬]*¬IG÷(?<home>\d+)¬IH÷(?<away>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    if (!m.Success)
        return 0; // or throw, depending on your preference

    int home = int.Parse(m.Groups["home"].Value);
    int away = int.Parse(m.Groups["away"].Value);
    return home + away;
}


static async Task<List<MatchResult>> GetMatchInfo(string input, HttpClient client)
{
    var pattern =
        @"KC÷(?<timestamp>\d+)¬" +
        @"(?:[^¬]*¬)*?KP÷(?<matchId>[^¬]+)¬" +
        @"(?:[^¬]*¬)*?KJ÷\*?(?<team1>[^¬]+)¬(?:[^¬]*¬)*?FH÷(?<team1Name>[^¬]+)¬" +
        @"(?:[^¬]*¬)*?KK÷\*?(?<team2>[^¬]+)¬(?:[^¬]*¬)*?FK÷(?<team2Name>[^¬]+)¬" +
        @"(?:[^¬]*¬)*?KL÷(?<score>\d+:\d+)";

    var matches = Regex.Matches(input, pattern, RegexOptions.Singleline);

    var results = new List<MatchResult>();

    foreach (Match match in matches)
    {
        var timestamp = long.Parse(match.Groups["timestamp"].Value);
        var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm");

        var matchId = match.Groups["matchId"].Value;

        var response = await client.GetAsync($"https://2.flashscore.ninja/2/x/feed/df_sui_1_{matchId}");
        var content = await response.Content.ReadAsStringAsync();
        
        var firstHalfGoals = ParseFirstHalfGoals(content);
        
        results.Add(new MatchResult
        {
            MatchId = matchId,
            Date = DateTime.Parse(date),
            HomeTeam = match.Groups["team1Name"].Value.Trim(),
            AwayTeam = match.Groups["team2Name"].Value.Trim(),
            Score = match.Groups["score"].Value,
            GoalsCount = match.Groups["score"].Value.Split(':').Sum(x => int.Parse(x)),
            FirstHalfGoals = firstHalfGoals
        });
    }

    results = results.GroupBy(x => x.Date).Select(x => x.First()).ToList();

    // Print the results
    foreach (var result in results)
    {
        Console.WriteLine($"{result.MatchId} | {result.Date}: {result.HomeTeam} vs {result.AwayTeam} => {result.Score}");
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


static void MakeHtml(List<UpcomingMatch> matches, Dictionary<string, DateTime> dict, DateTime date)
{
var html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <title>Football Match Stats</title>
  <style>
    body { font-family: Arial, sans-serif; background:#f4f4f4; padding:30px; }
    .filters {
      background:#fff; padding:12px 16px; border-radius:12px; margin-bottom:20px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.08); display:flex; gap:12px; flex-wrap:wrap; align-items:flex-end;
    }
    .filters label { font-size:14px; color:#333; display:flex; flex-direction:column; gap:4px; }
    .filters input[type=""number""] { width:80px; }
    .filters input[type=""datetime-local""] { width:230px; }
    .cond-row {
      display:flex; gap:8px; align-items:center; background:#fafafa; border:1px solid #eee; border-radius:10px; padding:8px 10px;
    }
    .cond-row label { margin:0; }
    .match-section {
      background:#fff; margin-bottom:40px; padding:20px; border-radius:12px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.1);
    }
    h1 { margin-top:0; }
    h2 { margin:0 0 4px 0; font-size:18px; }
    table { width:100%; border-collapse:collapse; margin-top:15px; }
    th, td { border:1px solid #ccc; text-align:center; padding:8px; font-size:14px; }
    th { background:#eee; }
    .pred { margin-top:8px; font-size:14px; color:#333; }
    .pred small { opacity:0.7; }
    .muted { opacity:0.5; }
    .hidden { display:none !important; }
  </style>
</head>
<body>
  <h1>Football Match Statistics (Last 5 Years)</h1>

  <div class=""filters"">
    <!-- Condition 1 -->
    <div class=""cond-row"">
      <label><input id=""c1on"" type=""checkbox"" checked> Use</label>
      <label>Type
        <select id=""c1k""><option value=""over"">Over</option><option value=""under"">Under</option></select>
      </label>
      <label>Threshold
        <select id=""c1t""><option value=""o05"">0.5</option><option value=""o15"">1.5</option><option value=""o25"">2.5</option></select>
      </label>
      <label>Half
        <select id=""c1h""><option value=""full"">Full</option><option value=""fh"">1st</option><option value=""sh"">2nd</option></select>
      </label>
      <label>Min %
        <input id=""c1min"" type=""number"" value=""60"" min=""0"" max=""100"" step=""1"">
      </label>
      <label>Max %
        <input id=""c1max"" type=""number"" value=""100"" min=""0"" max=""100"" step=""1"">
      </label>
    </div>

    <!-- Condition 2 -->
    <div class=""cond-row"">
      <label><input id=""c2on"" type=""checkbox""> Use</label>
      <label>Type
        <select id=""c2k""><option value=""over"">Over</option><option value=""under"">Under</option></select>
      </label>
      <label>Threshold
        <select id=""c2t""><option value=""o05"">0.5</option><option value=""o15"">1.5</option><option value=""o25"">2.5</option></select>
      </label>
      <label>Half
        <select id=""c2h""><option value=""full"">Full</option><option value=""fh"">1st</option><option value=""sh"">2nd</option></select>
      </label>
      <label>Min %
        <input id=""c2min"" type=""number"" value=""0"" min=""0"" max=""100"" step=""1"">
      </label>
      <label>Max %
        <input id=""c2max"" type=""number"" value=""100"" min=""0"" max=""100"" step=""1"">
      </label>
    </div>

    <!-- Condition 3 -->
    <div class=""cond-row"">
      <label><input id=""c3on"" type=""checkbox""> Use</label>
      <label>Type
        <select id=""c3k""><option value=""over"">Over</option><option value=""under"">Under</option></select>
      </label>
      <label>Threshold
        <select id=""c3t""><option value=""o05"">0.5</option><option value=""o15"">1.5</option><option value=""o25"">2.5</option></select>
      </label>
      <label>Half
        <select id=""c3h""><option value=""full"">Full</option><option value=""fh"">1st</option><option value=""sh"">2nd</option></select>
      </label>
      <label>Min %
        <input id=""c3min"" type=""number"" value=""0"" min=""0"" max=""100"" step=""1"">
      </label>
      <label>Max %
        <input id=""c3max"" type=""number"" value=""100"" min=""0"" max=""100"" step=""1"">
      </label>
    </div>

    <!-- Globals -->
    <label>Team
      <input id=""q"" type=""text"" placeholder=""Team name..."">
    </label>
    <label>Start <input id=""dtFrom"" type=""datetime-local""></label>
    <label>End <input id=""dtTo"" type=""datetime-local""></label>
    <span id=""count"" class=""muted""></span>
  </div>

  {match_section}

  <script>
    (function(){
      const countEl = document.getElementById('count');
      const qEl     = document.getElementById('q');
      const fromEl  = document.getElementById('dtFrom');
      const toEl    = document.getElementById('dtTo');

      const sections = Array.from(document.querySelectorAll('.match-section'));

      const conds = [
        { on:'c1on', kind:'c1k', thr:'c1t', half:'c1h', min:'c1min', max:'c1max' },
        { on:'c2on', kind:'c2k', thr:'c2t', half:'c2h', min:'c2min', max:'c2max' },
        { on:'c3on', kind:'c3k', thr:'c3t', half:'c3h', min:'c3min', max:'c3max' },
      ].map(ids => ({
        on:  document.getElementById(ids.on),
        kind:document.getElementById(ids.kind),
        thr: document.getElementById(ids.thr),
        half:document.getElementById(ids.half),
        min: document.getElementById(ids.min),
        max: document.getElementById(ids.max),
      }));

      function datasetKey(half, thr){
        const prefix = (half==='full'?'full':(half==='fh'?'fh':'sh'));
        const suffix = thr.toUpperCase(); // O05|O15|O25
        return prefix + suffix;           // e.g. 'fhO15'
      }

      function parseLocalDate(v){ if(!v) return NaN; return new Date(v).getTime(); }

      function condPass(el, c){
        if (!c.on.checked) return true; // disabled condition -> ignore
        const key = datasetKey(c.half.value, c.thr.value);
        const overProb = parseFloat(el.dataset[key]); // 0..1
        if (!isFinite(overProb)) return false;

        const overPct = overProb * 100;
        const value = (c.kind.value === 'over') ? overPct : (100 - overPct);

        let min = parseFloat(c.min.value);
        let max = parseFloat(c.max.value);
        if (!isFinite(min)) min = 0;
        if (!isFinite(max)) max = 100;
        if (max < min) { const t = min; min = max; max = t; } // auto-swap

        return value >= min && value <= max;
      }

      function apply(){
        const q = (qEl.value||'').trim().toLowerCase();
        const fromMs = parseLocalDate(fromEl.value);
        const toMs   = parseLocalDate(toEl.value);

        let shown = 0;

        for (const el of sections){
          const home = (el.dataset.home||'').toLowerCase();
          const away = (el.dataset.away||'').toLowerCase();
          const koStr = el.dataset.ko || '';
          const koMs  = koStr ? Date.parse(koStr) : NaN;

          let ok = true;

          // team search
          if (q && !home.includes(q) && !away.includes(q)) ok = false;

          // datetime (inclusive)
          if (ok && !isNaN(fromMs) && (!isNaN(koMs) && koMs < fromMs)) ok = false;
          if (ok && !isNaN(toMs)   && (!isNaN(koMs) && koMs > toMs))   ok = false;

          // all enabled conditions must pass
          if (ok){
            for (const c of conds){
              if (!condPass(el, c)) { ok = false; break; }
            }
          }

          el.classList.toggle('hidden', !ok);
          if (ok) shown++;
        }

        countEl.textContent = shown + ' match' + (shown === 1 ? '' : 'es') + ' shown';
      }

      const inputs = [
        qEl, fromEl, toEl,
        ...conds.flatMap(c => [c.on, c.kind, c.thr, c.half, c.min, c.max])
      ];
      inputs.forEach(el => el.addEventListener('input', apply));

      apply();
    })();
  </script>
</body>
</html>";


    var resultMatchSection = "";

    foreach (var match in matches)
    {
        if (match?.Results == null || match.Results.Count == 0)
            continue;

        var rowTpl = @"
<tr>
  <td>{year}</td>
  <td>{homeTeamName}</td>
  <td>{homeTotalGames}</td>
  <td>{homeOver0.5}</td>
  <td>{homeOver1.5}</td>
  <td>{homeOver2.5}</td>
  <td>{homeOverFirstHalf0.5}</td>
  <td>{homeOverFirstHalf1.5}</td>
  <td>{homeOverFirstHalf2.5}</td>
</tr>
<tr>
  <td>{year}</td>
  <td>{awayTeamName}</td>
  <td>{awayTotalGames}</td>
  <td>{awayOver0.5}</td>
  <td>{awayOver1.5}</td>
  <td>{awayOver2.5}</td>
  <td>{awayOverFirstHalf0.5}</td>
  <td>{awayOverFirstHalf1.5}</td>
  <td>{awayOverFirstHalf2.5}</td>
</tr>
<tr>
  <td>{year}</td>
  <td>H2H Games</td>
  <td>{h2hTotalGames}</td>
  <td>{h2hOver0.5}</td>
  <td>{h2hOver1.5}</td>
  <td>{h2hOver2.5}</td>
  <td>{h2hOverFirstHalf0.5}</td>
  <td>{h2hOverFirstHalf1.5}</td>
  <td>{h2hOverFirstHalf2.5}</td>
</tr>
";

        var matchSectionTpl = @"<div class=""match-section"" {dataAttrs}>
  <h2><a href='https://www.flashscore.com/match/football/{matchId}/#/match-summary/match-summary'>
    {homeTeamName} vs {awayTeamName}
  </a></h2>
  <div><small>{kickoff}</small></div>

  <table>
    <tr>
      <th>Year</th>
      <th>Team</th>
      <th>Total Games</th>
      <th>Over 0.5</th>
      <th>Over 1.5</th>
      <th>Over 2.5</th>
      <th>Over 1st Half 0.5</th>
      <th>Over 1st Half 1.5</th>
      <th>Over 1st Half 2.5</th>
    </tr>
    {resultData}
  </table>
  {overallPredictionBlock}
</div>";

        dict.TryGetValue(match.MatchId, out var ko);
        var kickoffTxt = ko == default ? "" : ko.ToString("yyyy-MM-dd HH:mm");

        var matchSection = matchSectionTpl
            .Replace("{homeTeamName}", match.HomeTeam)
            .Replace("{awayTeamName}", $"{match.AwayTeam} ({match.MatchId})")
            .Replace("{matchId}", match.MatchId)
            .Replace("{kickoff}", kickoffTxt);

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

            int homeTotal = yrGroup.Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
            int awayTotal = yrGroup.Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
            int h2hTotal  = yrGroup.Count(x =>
                               (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                               (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));

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

            int homeFirstHalfO05 = yrGroup.Count(x => x.FirstHalfGoals >= 1 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
            int homeFirstHalfO15 = yrGroup.Count(x => x.FirstHalfGoals >= 2 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
            int homeFirstHalfO25 = yrGroup.Count(x => x.FirstHalfGoals >= 3 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));

            int awayFirstHalfO05 = yrGroup.Count(x => x.FirstHalfGoals >= 1 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
            int awayFirstHalfO15 = yrGroup.Count(x => x.FirstHalfGoals >= 2 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
            int awayFirstHalfO25 = yrGroup.Count(x => x.FirstHalfGoals >= 3 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));

            int h2hFirstHalfO05 = yrGroup.Count(x => x.FirstHalfGoals >= 1 && (
                                (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));
            int h2hFirstHalfO15 = yrGroup.Count(x => x.FirstHalfGoals >= 2 && (
                                (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));
            int h2hFirstHalfO25 = yrGroup.Count(x => x.FirstHalfGoals >= 3 && (
                                (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

            yearData = yearData.Replace("{homeTotalGames}", homeTotal.ToString())
                               .Replace("{h2hTotalGames}", h2hTotal.ToString())
                               .Replace("{awayTotalGames}", awayTotal.ToString())
                               .Replace("{homeOver0.5}", homeO05.ToString())
                               .Replace("{homeOver1.5}", homeO15.ToString())
                               .Replace("{homeOver2.5}", homeO25.ToString())
                               .Replace("{awayOver0.5}", awayO05.ToString())
                               .Replace("{awayOver1.5}", awayO15.ToString())
                               .Replace("{awayOver2.5}", awayO25.ToString())
                               .Replace("{h2hOver0.5}", h2hO05.ToString())
                               .Replace("{h2hOver1.5}", h2hO15.ToString())
                               .Replace("{h2hOver2.5}", h2hO25.ToString())
                               .Replace("{homeOverFirstHalf0.5}", homeFirstHalfO05.ToString())
                               .Replace("{homeOverFirstHalf1.5}", homeFirstHalfO15.ToString())
                               .Replace("{homeOverFirstHalf2.5}", homeFirstHalfO25.ToString())
                               .Replace("{awayOverFirstHalf0.5}", awayFirstHalfO05.ToString())
                               .Replace("{awayOverFirstHalf1.5}", awayFirstHalfO15.ToString())
                               .Replace("{awayOverFirstHalf2.5}", awayFirstHalfO25.ToString())
                               .Replace("{h2hOverFirstHalf0.5}", h2hFirstHalfO05.ToString())
                               .Replace("{h2hOverFirstHalf1.5}", h2hFirstHalfO15.ToString())
                               .Replace("{h2hOverFirstHalf2.5}", h2hFirstHalfO25.ToString());

            resultData += yearData;
        }

        matchSection = matchSection.Replace("{resultData}", resultData);

        // --- Overall (since 2020) blended predictions for ≥0.5 / ≥1.5 / ≥2.5 ---
        var allRows = match.Results.Where(x => x.Date.Year >= 2020).ToList();

        int A_total = allRows.Count(x => x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam);
        int B_total = allRows.Count(x => x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam);
        int H_total = allRows.Count(x => (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                                         (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam));

        // Full-time
        int A_o05 = allRows.Count(x => x.GoalsCount >= 1 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o05 = allRows.Count(x => x.GoalsCount >= 1 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o05 = allRows.Count(x => x.GoalsCount >= 1 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        int A_o15 = allRows.Count(x => x.GoalsCount >= 2 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o15 = allRows.Count(x => x.GoalsCount >= 2 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o15 = allRows.Count(x => x.GoalsCount >= 2 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        int A_o25 = allRows.Count(x => x.GoalsCount >= 3 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o25 = allRows.Count(x => x.GoalsCount >= 3 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o25 = allRows.Count(x => x.GoalsCount >= 3 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        // First-half
        int A_o_First_05 = allRows.Count(x => x.FirstHalfGoals >= 1 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o_First_05 = allRows.Count(x => x.FirstHalfGoals >= 1 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o_First_05 = allRows.Count(x => x.FirstHalfGoals >= 1 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        int A_o_First_15 = allRows.Count(x => x.FirstHalfGoals >= 2 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o_First_15 = allRows.Count(x => x.FirstHalfGoals >= 2 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o_First_15 = allRows.Count(x => x.FirstHalfGoals >= 2 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        int A_o_First_25 = allRows.Count(x => x.FirstHalfGoals >= 3 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o_First_25 = allRows.Count(x => x.FirstHalfGoals >= 3 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o_First_25 = allRows.Count(x => x.FirstHalfGoals >= 3 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        // Second-half = full - firstHalf
        int A_o_Second_05 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 1 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o_Second_05 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 1 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o_Second_05 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 1 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        int A_o_Second_15 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 2 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o_Second_15 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 2 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o_Second_15 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 2 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        int A_o_Second_25 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 3 && (x.HomeTeam == match.HomeTeam || x.AwayTeam == match.HomeTeam));
        int B_o_Second_25 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 3 && (x.HomeTeam == match.AwayTeam || x.AwayTeam == match.AwayTeam));
        int H_o_Second_25 = allRows.Count(x => (x.GoalsCount - x.FirstHalfGoals) >= 3 && (
                            (x.HomeTeam == match.HomeTeam && x.AwayTeam == match.AwayTeam) ||
                            (x.HomeTeam == match.AwayTeam && x.AwayTeam == match.HomeTeam)));

        // smoothed rates (0..1)
        double pA05 = SmoothedRate(A_o05, A_total), pB05 = SmoothedRate(B_o05, B_total), pH05 = SmoothedRate(H_o05, H_total);
        double pA15 = SmoothedRate(A_o15, A_total), pB15 = SmoothedRate(B_o15, B_total), pH15 = SmoothedRate(H_o15, H_total);
        double pA25 = SmoothedRate(A_o25, A_total), pB25 = SmoothedRate(B_o25, B_total), pH25 = SmoothedRate(H_o25, H_total);

        double pAFirst05 = SmoothedRate(A_o_First_05, A_total), pBFirst05 = SmoothedRate(B_o_First_05, B_total), pHFirst05 = SmoothedRate(H_o_First_05, H_total);
        double pAFirst15 = SmoothedRate(A_o_First_15, A_total), pBFirst15 = SmoothedRate(B_o_First_15, B_total), pHFirst15 = SmoothedRate(H_o_First_15, H_total);
        double pAFirst25 = SmoothedRate(A_o_First_25, A_total), pBFirst25 = SmoothedRate(B_o_First_25, B_total), pHFirst25 = SmoothedRate(H_o_First_25, H_total);

        double pASecond05 = SmoothedRate(A_o_Second_05, A_total), pBSecond05 = SmoothedRate(B_o_Second_05, B_total), pHSecond05 = SmoothedRate(H_o_Second_05, H_total);
        double pASecond15 = SmoothedRate(A_o_Second_15, A_total), pBSecond15 = SmoothedRate(B_o_Second_15, B_total), pHSecond15 = SmoothedRate(H_o_Second_15, H_total);
        double pASecond25 = SmoothedRate(A_o_Second_25, A_total), pBSecond25 = SmoothedRate(B_o_Second_25, B_total), pHSecond25 = SmoothedRate(H_o_Second_25, H_total);

        double wA = WeightFromSamples(A_total, 40);
        double wB = WeightFromSamples(B_total, 40);
        double wH = WeightFromSamples(H_total, 10);

        double pOver05_all = Blend((pA05, wA), (pB05, wB), (pH05, wH));
        double pOver15_all = Blend((pA15, wA), (pB15, wB), (pH15, wH));
        double pOver25_all = Blend((pA25, wA), (pB25, wB), (pH25, wH));

        double pOver05_First_all = Blend((pAFirst05, wA), (pBFirst05, wB), (pHFirst05, wH));
        double pOver15_First_all = Blend((pAFirst15, wA), (pBFirst15, wB), (pHFirst15, wH));
        double pOver25_First_all = Blend((pAFirst25, wA), (pBFirst25, wB), (pHFirst25, wH));

        double pOver05_Second_all = Blend((pASecond05, wA), (pBSecond05, wB), (pHSecond05, wH));
        double pOver15_Second_all = Blend((pASecond15, wA), (pBSecond15, wB), (pHSecond15, wH));
        double pOver25_Second_all = Blend((pASecond25, wA), (pBSecond25, wB), (pHSecond25, wH));

        var overallBlock = $@"<div class=""pred"">
  <strong>Predictions (since 2020):</strong>
  ≥0.5 = {ToPct(pOver05_all)}, ≥1.5 = {ToPct(pOver15_all)}, ≥2.5 = {ToPct(pOver25_all)}
  <br/><small>
    A≥0.5:{ToPct(pA05)}, B≥0.5:{ToPct(pB05)}{(H_total>0 ? $", H2H≥0.5:{ToPct(pH05)}" : "")}
  </small>
</div><div class=""pred"">
  <strong>Predictions 1st Half (since 2020):</strong>
  ≥0.5 = {ToPct(pOver05_First_all)}, ≥1.5 = {ToPct(pOver15_First_all)}, ≥2.5 = {ToPct(pOver25_First_all)}
</div><div class=""pred"">
  <strong>Predictions 2nd Half (since 2020):</strong>
  ≥0.5 = {ToPct(pOver05_Second_all)}, ≥1.5 = {ToPct(pOver15_Second_all)}, ≥2.5 = {ToPct(pOver25_Second_all)}
</div>";
        
        var koIso = ko == default ? "" : ko.ToString("O"); // ISO 8601
        // Add data-* attributes used by filters (store raw 0..1)
        string DataAttr(string name, double v) => $@" data-{name}=""{v:0.000}""";
        var dataAttrs = string.Join("",
            new[]{
                $@"data-home=""{match.HomeTeam.Replace("\"","&quot;")}""",
                $@"data-away=""{match.AwayTeam.Replace("\"","&quot;")}""",
                $@"data-ko=""{koIso}""",
                DataAttr("full-o05",   pOver05_all),
                DataAttr("full-o15",   pOver15_all),
                DataAttr("full-o25",   pOver25_all),
                DataAttr("fh-o05",     pOver05_First_all),
                DataAttr("fh-o15",     pOver15_First_all),
                DataAttr("fh-o25",     pOver25_First_all),
                DataAttr("sh-o05",     pOver05_Second_all),
                DataAttr("sh-o15",     pOver15_Second_all),
                DataAttr("sh-o25",     pOver25_Second_all)
            });

        matchSection = matchSection.Replace("{overallPredictionBlock}", overallBlock)
                                   .Replace("{dataAttrs}", dataAttrs);

        resultMatchSection += matchSection;
    }

    var outPath = $"{date.Date:yyyy-MM-dd-}index.html";
    File.WriteAllText(outPath, html.Replace("{match_section}", resultMatchSection));
    Console.WriteLine($"Done -> {outPath}");
}




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


public class MatchResult
{
    public DateTime Date { get; set; }
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string Score { get; set; }
    public string MatchId { get; set; }
    public int GoalsCount { get; set; }
    public int FirstHalfGoals { get; set; }
}

public class UpcomingMatch
{
    public string HomeTeam { get; set; }
    public string AwayTeam { get; set; }
    public string MatchId { get; set; }
    public List<MatchResult> Results { get; set; }
}