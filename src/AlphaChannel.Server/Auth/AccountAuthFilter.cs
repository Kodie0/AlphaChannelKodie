using AlphaChannel.Server.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AlphaChannel.Server.Auth;

// Applied to a route group via .AddEndpointFilter<AccountAuthFilter>() rather than repeated in
// every handler - reads Authorization: Bearer, validates it, and stashes the resolved Account in
// HttpContext.Items for the handler to pull out via HttpContext.GetAccount().
internal sealed class AccountAuthFilter(AccountService accounts) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var auth = context.HttpContext.Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth["Bearer ".Length..] : null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return TypedResults.Unauthorized();
        }

        var account = await accounts.ValidateTokenAsync(token, context.HttpContext.RequestAborted);
        if (account is null)
        {
            Console.Error.WriteLine($"[FriendDiag] {context.HttpContext.Request.Path} 401: token invalid/expired/banned (tokenPrefix={token[..Math.Min(8, token.Length)]}...)");
            return TypedResults.Unauthorized();
        }

        context.HttpContext.Items[nameof(Account)] = account;
        return await next(context);
    }
}

internal static class HttpContextAccountExtensions
{
    public static Account GetAccount(this HttpContext context) =>
        (Account)context.Items[nameof(Account)]!;
}
