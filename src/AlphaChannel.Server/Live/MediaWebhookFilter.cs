namespace AlphaChannel.Server.Live;

// Same shared-secret-header model as Admin/AdminTokenFilter.cs, checking a different env var -
// MediaMTX is the only caller of the media-only endpoint group this guards (publish-auth plus the
// live/offline hooks), never an account bearer token.
internal sealed class MediaWebhookFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var secret = configuration["MEDIAMTX_WEBHOOK_SECRET"];
        if (string.IsNullOrEmpty(secret) || context.HttpContext.Request.Headers["X-Media-Secret"] != secret)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
