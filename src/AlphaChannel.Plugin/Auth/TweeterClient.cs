using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlphaChannel.Contracts;

namespace AlphaChannel.Plugin.Auth;

internal sealed class TweeterClient(Configuration configuration)
{
    private HttpClient Http(string bearerToken)
    {
        var http = new HttpClient { BaseAddress = new Uri(configuration.RelayServerUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return http;
    }

    internal async Task<PostDto?> CreatePostAsync(string bearerToken, string body, string? parentPostId = null, string? imageUrl = null)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsJsonAsync("/posts", new CreatePostRequest(body, parentPostId, imageUrl)).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PostDto>().ConfigureAwait(false) : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Tweeter] post failed: {exception.Message}");
            return null;
        }
    }

    internal async Task<PostDto?> RepostAsync(string bearerToken, string postId, string? quoteBody = null)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsJsonAsync($"/posts/{postId}/repost", new RepostRequest(quoteBody)).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PostDto>().ConfigureAwait(false) : null;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Tweeter] repost failed: {exception.Message}");
            return null;
        }
    }

    internal Task<TimelinePage?> GetRepliesAsync(string bearerToken, string postId) =>
        GetAsync<TimelinePage>(bearerToken, $"/posts/{postId}/replies");

    internal Task<TimelinePage?> SearchByHashtagAsync(string bearerToken, string hashtag) =>
        GetAsync<TimelinePage>(bearerToken, $"/posts/search?hashtag={Uri.EscapeDataString(hashtag)}");

    internal Task<bool> DeletePostAsync(string bearerToken, string postId) => DeleteAsync(bearerToken, $"/posts/{postId}");

    internal Task<TimelinePage?> GetTimelineAsync(string bearerToken, long? before) =>
        GetAsync<TimelinePage>(bearerToken, before is { } cursor ? $"/timeline?before={cursor}" : "/timeline");

    internal Task<TimelinePage?> GetAccountPostsAsync(string bearerToken, string accountId, long? before) =>
        GetAsync<TimelinePage>(bearerToken, before is { } cursor
            ? $"/accounts/{accountId}/posts?before={cursor}"
            : $"/accounts/{accountId}/posts");

    internal Task<bool> LikeAsync(string bearerToken, string postId) => PostAsync(bearerToken, $"/posts/{postId}/like");

    internal Task<bool> UnlikeAsync(string bearerToken, string postId) => DeleteAsync(bearerToken, $"/posts/{postId}/like");

    internal Task<bool> FollowAsync(string bearerToken, string accountId) => PostAsync(bearerToken, $"/follows/{accountId}");

    internal Task<bool> UnfollowAsync(string bearerToken, string accountId) => DeleteAsync(bearerToken, $"/follows/{accountId}");

    internal Task<FollowSummaryDto[]?> GetFollowingAsync(string bearerToken) => GetAsync<FollowSummaryDto[]>(bearerToken, "/follows/following");

    internal Task<FollowSummaryDto[]?> GetFollowersAsync(string bearerToken) => GetAsync<FollowSummaryDto[]>(bearerToken, "/follows/followers");

    private async Task<bool> PostAsync(string bearerToken, string path)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.PostAsync(path, null).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Tweeter] request to {path} failed: {exception.Message}");
            return false;
        }
    }

    private async Task<bool> DeleteAsync(string bearerToken, string path)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.DeleteAsync(path).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Tweeter] request to {path} failed: {exception.Message}");
            return false;
        }
    }

    private async Task<T?> GetAsync<T>(string bearerToken, string path)
    {
        using var http = Http(bearerToken);
        try
        {
            var response = await http.GetAsync(path).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>().ConfigureAwait(false) : default;
        }
        catch (Exception exception)
        {
            AepLog.Warning($"[Tweeter] request to {path} failed: {exception.Message}");
            return default;
        }
    }
}
