namespace AlphaChannel.Server.Twitch;

// Polls Helix on a fixed interval and republishes into TwitchTrendingService's cache - Helix's real
// rate limit is 2 req/sec/app, so this is nowhere near it, but there's no reason to poll faster than
// the UI would meaningfully change either.
internal sealed class TwitchTrendingRefreshService(TwitchHelixClient helix, TwitchTrendingService trending) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(75);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var streams = await helix.GetTopStreamsAsync(20, stoppingToken).ConfigureAwait(false);
                trending.Update([.. streams]);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Best-effort - a failed refresh just leaves the previous cached list in place
                // until the next tick succeeds, rather than tearing down the whole server.
                Console.Error.WriteLine($"[Twitch] Trending refresh failed: {exception.Message}");
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
