namespace OpsContext.Agents.Tools;

public interface IDemoService
{
    bool IsAvailable { get; }
    Task SeedSampleDataAsync(CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}

public sealed class NullDemoService : IDemoService
{
    public bool IsAvailable => false;
    public Task SeedSampleDataAsync(CancellationToken ct) => Task.CompletedTask;
    public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
}
