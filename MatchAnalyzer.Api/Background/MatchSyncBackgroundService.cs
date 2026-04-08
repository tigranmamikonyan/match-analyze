using MatchAnalyzer.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
                var updatedCount = await parserService.UpdateUnparsedMatchesAsync();
                await Task.Delay(60 * 1000, stoppingToken);
                int count = await parserService.SyncUpcomingMatches(1);
                int countTournament = await parserService.UpdateMatchesTournamentsAsync();

                RunPythonScript("/var/www/matchparser/ai_model/.venv/bin/python", "/var/www/matchparser/ai_model/predict_today_v3.py");
                RunPythonScript("/var/www/matchparser/ai_model/.venv/bin/python", "/var/www/matchparser/ai_model/BeastModel10/final_daily_predictor.py");
                RunPythonScript("/var/www/matchparser/ai_model/.venv/bin/python", "/var/www/matchparser/ai_model/FinalPredictionXGBoost/final_daily_predictor.py");
                _logger.LogInformation(
                    "Background Sync completed. Processed {Count} matches, updated {UpdatedCount} unparsed matches.",
                    count, updatedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Match Sync in background.");
            }
        }
    }

    void RunPythonScript(string pythonPath, string scriptPath)
    {
        // Optional but highly recommended: Verify files exist before attempting to run
        if (!File.Exists(pythonPath))
        {
            Console.WriteLine($"❌ System Error: Python executable not found at '{pythonPath}'");
            return;
        }

        if (!File.Exists(scriptPath))
        {
            Console.WriteLine($"❌ System Error: Python script not found at '{scriptPath}'");
            return;
        }

        // Configure the process to run silently and redirect output
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = scriptPath, // Add extra arguments here if your script needs them later
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using (Process process = new Process
                   {
                       StartInfo = startInfo
                   })
            {
                // Stream Standard Output real-time
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine(e.Data);
                    }
                };

                // Stream Error Output real-time in Red
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[PYTHON ERROR]: {e.Data}");
                        Console.ResetColor();
                    }
                };

                // Start the process
                process.Start();

                // Begin asynchronous reading of the streams
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Keep the C# application alive until the Python script finishes its job
                process.WaitForExit();

                Console.WriteLine($"\n✅ Script execution completed. Exit Code: {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ A C# Exception occurred while trying to launch Python: {ex.Message}");
        }
    }
}