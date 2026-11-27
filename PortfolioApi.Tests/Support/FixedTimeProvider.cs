namespace PortfolioApi.Tests.Support;

/// <summary>A clock that always says the same time, so "now" in a test is known in advance.</summary>
public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
