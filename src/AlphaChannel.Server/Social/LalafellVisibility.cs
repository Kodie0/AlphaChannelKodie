using AlphaChannel.Server.Data;

namespace AlphaChannel.Server.Social;

// Shared by FriendService today, and by Activity/DM discovery once those land - "social apps only"
// per the feature's own framing, this must never be consulted by watch-along/presence-in-room.
internal static class LalafellVisibility
{
    // Per-viewer filter: does this specific viewer want target's Lalafell-flagged content hidden?
    // HideLalafellFromNonLalafell is an admin-wide override that forces it for everyone regardless
    // of what the viewer answered at onboarding.
    //
    // Temporarily disabled (always false) alongside LalafellGateFilter - see that file's comment.
    // The settings/preference plumbing (ServerSettings.HideLalafellFromNonLalafell,
    // Account.WantsToSeeLalafellContent) stays intact for when this is re-enabled.
    public static bool IsHiddenFrom(Account viewer, Account target, ServerSettings settings) => false;

    // Separate axis from the above: can this account use social features AT ALL yet? Only
    // meaningful for the account's own actions, not for filtering how others see them.
    public static bool CanUseSocialFeatures(Account account) =>
        account.LalafellSocialStatus is LalafellSocialStatus.NotApplicable or LalafellSocialStatus.Approved;
}
