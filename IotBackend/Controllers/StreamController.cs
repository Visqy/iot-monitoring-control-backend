using System.Threading.Channels;
using IotBackend.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IotBackend.Controllers;

[ApiController]
[Route("api/stream")]
[Authorize]
public sealed class StreamController : ControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private readonly RealtimeBroadcaster _broadcaster;

    public StreamController(RealtimeBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    [HttpGet]
    public async Task Get(CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        await Response.Body.FlushAsync(cancellationToken);

        var channel = Channel.CreateUnbounded<string>();
        var clientId = _broadcaster.Subscribe(channel.Writer);

        try
        {
            var readTask = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();

            while (!cancellationToken.IsCancellationRequested)
            {
                var delayTask = Task.Delay(HeartbeatInterval, cancellationToken);
                var completed = await Task.WhenAny(readTask, delayTask);

                if (completed == readTask)
                {
                    if (!await readTask)
                    {
                        break;
                    }

                    while (channel.Reader.TryRead(out var frame))
                    {
                        await Response.WriteAsync(frame, cancellationToken);
                    }

                    readTask = channel.Reader.WaitToReadAsync(cancellationToken).AsTask();
                }
                else
                {
                    await Response.WriteAsync(": ping\n\n", cancellationToken);
                }

                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _broadcaster.Unsubscribe(clientId);
        }
    }
}
