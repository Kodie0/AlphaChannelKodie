using AlphaChannel.Contracts;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Social;

// Persistent hangout spaces - a named, saved screen placement a player can revisit or share, one
// step up from the purely-local Configuration.ScreenPositionPreset on the client. Friends-only
// visibility, same "accepted friendship is the bar" reasoning as PluginHubService/DmService.
internal sealed class VenueService(IDbContextFactory<AlphaChannelDbContext> dbFactory, ActivityService activity)
{
    private const int MaxVenuesPerAccount = 50;

    public async Task<VenueDto?> CreateAsync(Guid ownerId, CreateVenueRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var count = await db.Venues.CountAsync(v => v.OwnerAccountId == ownerId, cancellationToken);
        if (count >= MaxVenuesPerAccount)
        {
            return null;
        }

        var venue = new Venue
        {
            Id = Guid.NewGuid(),
            OwnerAccountId = ownerId,
            Name = name[..Math.Min(name.Length, 48)],
            TerritoryTypeId = request.TerritoryTypeId,
            ScreenX = request.ScreenX,
            ScreenY = request.ScreenY,
            ScreenZ = request.ScreenZ,
            ScreenYaw = request.ScreenYaw,
            ScreenScale = request.ScreenScale,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Venues.Add(venue);
        await db.SaveChangesAsync(cancellationToken);

        // Friends-fanout only (no TargetAccountId) - a new venue is "something a friend did," shown
        // in friends' feeds the same way StartedWatching/JoinedWatchAlong already are, not a direct
        // notification to any one account.
        await activity.RecordAsync(ownerId, ActivityEventType.VenueSaved, name, cancellationToken);

        return ToDto(venue);
    }

    public async Task<List<VenueDto>> GetMineAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Venues
            .Where(v => v.OwnerAccountId == ownerId)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(v => new VenueDto(v.Id.ToString(), v.Name, v.TerritoryTypeId,
                v.ScreenX, v.ScreenY, v.ScreenZ, v.ScreenYaw, v.ScreenScale, ToUnixSeconds(v.CreatedAtUtc)))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid ownerId, Guid venueId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var venue = await db.Venues.FirstOrDefaultAsync(v => v.Id == venueId && v.OwnerAccountId == ownerId, cancellationToken);
        if (venue is null)
        {
            return false;
        }

        db.Venues.Remove(venue);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Null means "not friends" (caller shouldn't see anything, not even an empty list) vs. an empty
    // array meaning "friends, but no venues saved" - same distinction as PluginHubService.
    // GetFriendPluginsAsync.
    public async Task<List<VenueDto>?> GetFriendVenuesAsync(Guid callerId, Guid friendAccountId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var isFriend = await db.Friendships.AnyAsync(f =>
            f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterAccountId == callerId && f.AddresseeAccountId == friendAccountId) ||
             (f.RequesterAccountId == friendAccountId && f.AddresseeAccountId == callerId)), cancellationToken);
        if (!isFriend)
        {
            return null;
        }

        return await db.Venues
            .Where(v => v.OwnerAccountId == friendAccountId)
            .OrderByDescending(v => v.CreatedAtUtc)
            .Select(v => new VenueDto(v.Id.ToString(), v.Name, v.TerritoryTypeId,
                v.ScreenX, v.ScreenY, v.ScreenZ, v.ScreenYaw, v.ScreenScale, ToUnixSeconds(v.CreatedAtUtc)))
            .ToListAsync(cancellationToken);
    }

    private static VenueDto ToDto(Venue v) => new(v.Id.ToString(), v.Name, v.TerritoryTypeId,
        v.ScreenX, v.ScreenY, v.ScreenZ, v.ScreenYaw, v.ScreenScale, ToUnixSeconds(v.CreatedAtUtc));

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
