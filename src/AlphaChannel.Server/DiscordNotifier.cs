using System.Net.Http.Json;

namespace AlphaChannel.Server;

// Posts to a Discord incoming webhook for team review/discussion - notification only, never the
// mechanism for taking action (approve/deny happens through the admin UI's own endpoints, gated by
// X-Admin-Token). No-ops quietly if DISCORD_LALAFELL_WEBHOOK_URL isn't configured, same "optional,
// just skip it" posture as ADMIN_TOKEN not being set.
internal sealed class DiscordNotifier(HttpClient httpClient, string? webhookUrl)
{
    public async Task NotifyAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return;
        }

        try
        {
            await httpClient.PostAsJsonAsync(webhookUrl, new { content }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[Discord] webhook post failed: {exception.Message}");
        }
    }
}
