using MatchAnalyzer.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MatchAnalyzer.Api.Background;

public class MatchSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MatchSyncBackgroundService> _logger;

    public MatchSyncBackgroundService(IServiceProvider serviceProvider, ILogger<MatchSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Match Sync Background Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddHours(1); // 01:00 AM today
            if (now >= nextRun)
            {
                nextRun = nextRun.AddDays(1); // 01:00 AM tomorrow
            }

            var delay = nextRun - now;
            _logger.LogInformation("Next sync scheduled for {NextRun} (in {Delay})", nextRun, delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await DoWorkAsync(stoppingToken);
        }
    }

    private async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Match Sync Background Service is working.");

        using (var scope = _serviceProvider.CreateScope())
        {
            var parserService = scope.ServiceProvider.GetRequiredService<MatchParserService>();
            try
            {
                // Sync for today + 1 day ahead just in case, or whatever logic is preferred.
                // User said "upcoming game for that day".
                // Defaulting to 1 day ahead (tomorrow's games, or today's?)
                // The original code did `addedDaysCount = 1`.
                
                int count = await parserService.SyncUpcomingMatches(1); 
                _logger.LogInformation("Background Sync completed. Processed {Count} matches.", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Match Sync in background.");
            }
        }
    }
}
