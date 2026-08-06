using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Data;

internal sealed class AlphaChannelDbContext(DbContextOptions<AlphaChannelDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountCharacter> AccountCharacters => Set<AccountCharacter>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<DmConversation> DmConversations => Set<DmConversation>();
    public DbSet<DmMessage> DmMessages => Set<DmMessage>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<ActivityReadMarker> ActivityReadMarkers => Set<ActivityReadMarker>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ServerSettings> Settings => Set<ServerSettings>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<Follow> Follows => Set<Follow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.HasIndex(a => a.Handle).IsUnique();
            e.HasIndex(a => a.InviteCode).IsUnique();
        });

        modelBuilder.Entity<AccountCharacter>(e =>
        {
            // A character can only ever be linked to one account - this is what stops someone from
            // dodging a ban by re-verifying the same character under a brand-new account.
            e.HasIndex(c => new { c.CharacterName, c.World }).IsUnique();
            e.HasIndex(c => c.AccountId);
        });

        modelBuilder.Entity<AuthToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.AccountId);
        });

        modelBuilder.Entity<Friendship>(e =>
        {
            e.HasIndex(f => new { f.RequesterAccountId, f.AddresseeAccountId }).IsUnique();
            e.HasIndex(f => f.AddresseeAccountId);
        });

        modelBuilder.Entity<Block>(e =>
        {
            e.HasIndex(b => new { b.BlockerAccountId, b.BlockedAccountId }).IsUnique();
        });

        modelBuilder.Entity<DmConversation>(e =>
        {
            e.HasIndex(c => new { c.AccountAId, c.AccountBId }).IsUnique();
        });

        modelBuilder.Entity<DmMessage>(e =>
        {
            e.HasIndex(m => new { m.ConversationId, m.SentAtUtc });
        });

        modelBuilder.Entity<ActivityEvent>(e =>
        {
            e.HasIndex(a => new { a.AccountId, a.CreatedAtUtc });
        });

        modelBuilder.Entity<ActivityReadMarker>(e =>
        {
            e.HasKey(m => m.AccountId);
        });

        modelBuilder.Entity<Report>(e =>
        {
            e.HasIndex(r => r.Status);
        });

        modelBuilder.Entity<Post>(e =>
        {
            e.HasIndex(p => new { p.AuthorAccountId, p.CreatedAtUtc });
        });

        modelBuilder.Entity<PostLike>(e =>
        {
            e.HasIndex(l => new { l.PostId, l.AccountId }).IsUnique();
        });

        modelBuilder.Entity<Follow>(e =>
        {
            e.HasIndex(f => new { f.FollowerAccountId, f.FolloweeAccountId }).IsUnique();
            e.HasIndex(f => f.FolloweeAccountId);
        });

        // Seeds the one settings row via migration data rather than app-startup logic, so it's
        // guaranteed to exist the moment the schema exists - no "first request after a fresh
        // deploy" race to worry about.
        modelBuilder.Entity<ServerSettings>().HasData(new ServerSettings
        {
            Id = ServerSettings.SingletonId,
            HideLalafellFromNonLalafell = false,
        });
    }
}
