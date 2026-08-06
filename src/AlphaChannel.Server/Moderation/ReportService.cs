using System.Security.Cryptography;
using System.Text;
using AlphaChannel.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AlphaChannel.Server.Moderation;

// Submission side only - the admin review queue (GET /admin/reports, resolve actions) is a
// separate piece (see Admin/ReportAdminEndpoints). This is what verifies a DM-message reveal
// against DmMessage.CommitmentTag at the moment a report is filed, per that field's doc comment.
internal sealed class ReportService(IDbContextFactory<AlphaChannelDbContext> dbFactory)
{
    public async Task<Guid> SubmitAsync(
        Guid reporterId, string category, string? note, Guid? targetAccountId, Guid? targetMessageId,
        string? revealedBody, string? frankingKeyBase64, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        bool? frankingVerified = null;
        if (targetMessageId is { } messageId && revealedBody is not null && frankingKeyBase64 is not null)
        {
            var message = await db.DmMessages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
            if (message is not null)
            {
                var frankingKey = Convert.FromBase64String(frankingKeyBase64);
                var recomputed = HMACSHA256.HashData(frankingKey, Encoding.UTF8.GetBytes(revealedBody));
                frankingVerified = CryptographicOperations.FixedTimeEquals(recomputed, message.CommitmentTag);
            }
        }

        var report = new Report
        {
            Id = Guid.NewGuid(),
            ReporterAccountId = reporterId,
            ReportedAccountId = targetAccountId ?? Guid.Empty,
            ReportedMessageId = targetMessageId,
            Reason = category,
            Details = note,
            Status = ReportStatus.Open,
            CreatedAtUtc = DateTime.UtcNow,
            RevealedBody = revealedBody,
            FrankingKeyBase64 = frankingKeyBase64,
            FrankingVerified = frankingVerified,
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        return report.Id;
    }
}
