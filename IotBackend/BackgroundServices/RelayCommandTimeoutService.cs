using IotBackend.Options;
using IotBackend.Repositories;
using Microsoft.Extensions.Options;

namespace IotBackend.BackgroundServices;

public sealed class RelayCommandTimeoutService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RelayOptions _options;
    private readonly ILogger<RelayCommandTimeoutService> _logger;

    public RelayCommandTimeoutService(
        IServiceScopeFactory scopeFactory,
        IOptions<RelayOptions> options,
        ILogger<RelayCommandTimeoutService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutScanIntervalSeconds));
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.CommandTimeoutSeconds));

        using var timer = new PeriodicTimer(interval);

        while (await WaitForNextTickAsync(timer, stoppingToken))
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow - timeout;

                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<RelayCommandRepository>();
                var affected = await repository.MarkTimedOutStaleAsync(cutoff, stoppingToken);

                if (affected > 0)
                {
                    _logger.LogInformation(
                        "{Count} relay command ditandai timeout (tanpa konfirmasi > {Seconds}s).",
                        affected, timeout.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjalankan scan timeout relay_commands.");
            }
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
