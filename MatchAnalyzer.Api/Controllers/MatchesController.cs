using MatchAnalyzer.Api.Data;
using MatchAnalyzer.Api.Models;
using MatchAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatchAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MatchesController : ControllerBase
{
    private readonly MatchParserService _parserService;
    private readonly ILogger<MatchesController> _logger;
    private readonly MatchAnalysisService _analysisService;

    public MatchesController(MatchParserService parserService, MatchAnalysisService analysisService, ILogger<MatchesController> logger)
    {
        _parserService = parserService;
        _analysisService = analysisService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Match>>> GetMatches([FromQuery] DateTime? date)
    {
        var matches = await _parserService.GetMatchesAsync(date);
        return Ok(matches);
    }

    [HttpPost("sync")]
    public async Task<ActionResult<int>> SyncMatches([FromQuery] int days = 1)
    {
        try
        {
            var count = await _parserService.SyncUpcomingMatches(days);
            return Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            return StatusCode(500, "Sync failed: " + ex.Message);
        }
    }

    [HttpPost("sync-unparsed")]
    public async Task<ActionResult<int>> SyncUnparsedMatches()
    {
        try
        {
            var count = await _parserService.UpdateUnparsedMatchesAsync();
            return Ok(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync unparsed failed");
            return StatusCode(500, "Sync unparsed failed: " + ex.Message);
        }
    }

    [HttpGet("{id}/analysis")]
    public async Task<ActionResult<MatchAnalysisDto>> GetAnalysis(int id)
    {
        var result = await _analysisService.AnalyzeMatchAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost("search")]
    public async Task<ActionResult<List<Match>>> Search([FromBody] SearchRequest request)
    {
        // 1. Get candidate matches from DB
        var matches = await _parserService.SearchMatchesAsync(request);

        // 2. If no advanced conditions, return immediately
        if (request.Conditions == null || !request.Conditions.Any(c => c.Enabled))
        {
            return Ok(matches);
        }

        // 3. Bulk Analyze all candidates
        // This is much faster than doing it one by one as it fetches history in 1 query
        var analysisResults = await _analysisService.AnalyzeMatchesBulkAsync(matches);
        
        // 4. Filter by conditions
        var filteredMatches = new List<Match>();
        foreach (var match in matches)
        {
            if (analysisResults.TryGetValue(match, out var analysis))
            {
                if (_analysisService.MeetsConditions(analysis, request.Conditions))
                {
                    filteredMatches.Add(match);
                }
            }
        }

        return Ok(filteredMatches);
    }
}
