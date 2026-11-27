using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PortfolioApi.GitHub;
using PortfolioApi.Services;

namespace PortfolioApi.Tests.Unit;

/// <summary>
/// The background service on its own, with a fake sync: does it sync at start-up, does it respect
/// "SyncEnabled = false", and does an error leave the app running?
/// </summary>
public class CommitSyncBackgroundServiceTests
{
    private static CommitSyncBackgroundService CreateService(FakeSync sync, bool enabled)
    {
        var services = new ServiceCollection()
            .AddScoped<ICommitSyncService>(_ => sync)
            .BuildServiceProvider();
        var options = Options.Create(new GitHubOptions { SyncEnabled = enabled, SyncIntervalMinutes = 60 });
        return new CommitSyncBackgroundService(services.GetRequiredService<IServiceScopeFactory>(), options,
            NullLogger<CommitSyncBackgroundService>.Instance);
    }

    [Fact]
    public async Task Start_SyncEnabled_SyncsOnceRightAway()
    {
        // Arrange
        var sync = new FakeSync();
        var service = CreateService(sync, enabled: true);

        // Act
        await service.StartAsync(CancellationToken.None);
        await sync.FirstCall.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        // Assert: once at start-up; the next one would only come after 60 minutes.
        Assert.Equal(1, sync.Calls);
    }

    [Fact]
    public async Task Start_SyncDisabled_NeverSyncs()
    {
        // Arrange
        var sync = new FakeSync();
        var service = CreateService(sync, enabled: false);

        // Act
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(0, sync.Calls);
    }

    [Fact]
    public async Task Start_SyncThrows_TheServiceKeepsRunning()
    {
        // Arrange: for example the database is down.
        var sync = new FakeSync(throwOnSync: true);
        var service = CreateService(sync, enabled: true);

        // Act
        await service.StartAsync(CancellationToken.None);
        await sync.FirstCall.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50); // Give the service a moment to handle the error.

        // Assert: the error was caught and logged, the service waits for the next round.
        Assert.False(service.ExecuteTask!.IsCompleted);
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>Hand-made fake of the sync: counts calls and can throw like a broken database.</summary>
    private sealed class FakeSync(bool throwOnSync = false) : ICommitSyncService
    {
        private readonly TaskCompletionSource _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task FirstCall => _firstCall.Task;

        public Task<CommitSyncResult> SyncAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            _firstCall.TrySetResult();
            return throwOnSync
                ? throw new InvalidOperationException("Database is down (fake).")
                : Task.FromResult(new CommitSyncResult(CommitSyncStatus.Synced));
        }
    }
}
