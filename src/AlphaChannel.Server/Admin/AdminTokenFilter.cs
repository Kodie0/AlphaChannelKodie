namespace AlphaChannel.Server.Admin;

// Same shared-secret model the original /admin/reset-username endpoint used inline - reused here
// as a filter now that there are several admin endpoints instead of just one. "A few people" (per
// the feature request this backs) share this one token; it deliberately isn't a full multi-user
// admin account system, matching this project's existing minimal-tooling posture.
internal sealed class AdminTokenFilter(IConfiguration configuration) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var adminToken = configuration["ADMIN_TOKEN"];
        if (string.IsNullOrEmpty(adminToken) || context.HttpContext.Request.Headers["X-Admin-Token"] != adminToken)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
