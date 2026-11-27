using Microsoft.Extensions.Options;
using PortfolioApi.GitHub;

namespace PortfolioApi.Services;

/// <summary>
/// Runs the commit sync once at start-up and then every <see cref="GitHubOptions.SyncIntervalMinutes"/>.
/// Syncing at start-up matters on hosting that puts an idle app to sleep: the first visitor wakes it up
/// and the blog is up to date again.
/// </summary>
public class CommitSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<GitHubOptions> options,
    ILogger<CommitSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SyncEnabled)
        {
            logger.LogInformation("Commit sync is switched off (GitHub:SyncEnabled = false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.SyncIntervalMinutes));
        do
        {
            await SyncOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // A background service lives for the whole app, a DbContext only for one unit of work:
            // so every sync gets its own scope (and with it its own DbContext).
            await using var scope = scopeFactory.CreateAsyncScope();
            var sync = scope.ServiceProvider.GetRequiredService<ICommitSyncService>();
            await sync.SyncAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // An unexpected error (for example the database) must not stop the app or the next sync.
            logger.LogError(ex, "Commit sync failed.");
        }
    }
}
