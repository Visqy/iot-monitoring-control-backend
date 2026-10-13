using IotBackend.Options;
using IotBackend.Repositories;
using Microsoft.Extensions.Options;

namespace IotBackend.BackgroundServices;

public sealed class DeviceOfflineSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DeviceOfflineOptions _options;
    private readonly ILogger<DeviceOfflineSweepService> _logger;

    public DeviceOfflineSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<DeviceOfflineOptions> options,
        ILogger<DeviceOfflineSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.SweepIntervalSeconds));
        var threshold = TimeSpan.FromSeconds(Math.Max(1, _options.OfflineThresholdSeconds));

        using var timer = new PeriodicTimer(interval);

        while (await WaitForNextTickAsync(timer, stoppingToken))
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow - threshold;

                using var scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<DeviceStateRepository>();
                var affected = await repository.MarkOfflineStaleAsync(cutoff, stoppingToken);

                if (affected > 0)
                {
                    _logger.LogInformation(
                        "{Count} device ditandai offline (tidak ada pesan > {Seconds}s).",
                        affected, threshold.TotalSeconds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gagal menjalankan scan offline device_current_state.");
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
