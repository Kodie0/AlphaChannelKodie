using AlphaChannel.Contracts;
using AlphaChannel.Server.Auth;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

// Blind key relay - the server stores each account's long-term ECDH public key and hands it out to
// friends, but never sees a private key. See DmMessage's doc comment for the static-static ECDH
// scheme this supports.
internal static class KeyEndpoints
{
    public static void MapKeyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/keys").AddEndpointFilter<AccountAuthFilter>().AddEndpointFilter<LalafellGateFilter>();

        group.MapPut("/me", async (UploadPublicKeyRequest request, HttpContext context, IDbContextFactory<AlphaChannelDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var account = await db.Accounts.FirstAsync(a => a.Id == context.GetAccount().Id, ct);
            account.DmPublicKey = Convert.FromBase64String(request.PublicKeyBase64);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapGet("/me", async (HttpContext context, IDbContextFactory<AlphaChannelDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var account = await db.Accounts.FirstAsync(a => a.Id == context.GetAccount().Id, ct);
            return account.DmPublicKey is { } key ? Results.Ok(new PublicKeyDto(Convert.ToBase64String(key))) : Results.NotFound();
        });

        group.MapGet("/users/{accountId:guid}", async (Guid accountId, IDbContextFactory<AlphaChannelDbContext> dbFactory, CancellationToken ct) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            return account?.DmPublicKey is { } key ? Results.Ok(new PublicKeyDto(Convert.ToBase64String(key))) : Results.NotFound();
        });
    }
}
