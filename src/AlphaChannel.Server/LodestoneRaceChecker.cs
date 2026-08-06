using System.Text.RegularExpressions;

namespace AlphaChannel.Server;

// Best-effort, advisory-only corroboration of a character's race against Lodestone's public
// character search - never blocks sign-in or anything else, only ever sets
// Account.LodestoneRaceMismatch for an admin to notice during Lalafell review. Deliberately not a
// hard dependency: Lodestone's HTML isn't a stable contract, this can start silently failing if
// their markup changes, and that's fine given how it's used - see AccountService's fire-and-forget
// call site. NOT verified against a live Lodestone response during development (no network access
// to the real site from this environment) - the CSS class names/markup shape below are a best
// guess from Lodestone's known structure and should be spot-checked against a live character page
// before relying on this for anything beyond "advisory."
internal sealed partial class LodestoneRaceChecker(HttpClient httpClient)
{
    private static readonly string[] KnownRaces =
        ["Hyur", "Elezen", "Lalafell", "Miqo'te", "Roegadyn", "Au Ra", "Hrothgar", "Viera"];

    // Returns null if the lookup was inconclusive for any reason (network error, character not
    // found, unrecognized page shape) - callers must treat null as "couldn't check," not "not
    // Lalafell."
    public async Task<string?> TryGetRaceAsync(string characterName, string world, CancellationToken cancellationToken)
    {
        try
        {
            var searchUrl = $"https://na.finalfantasyxiv.com/lodestone/character/?q={Uri.EscapeDataString(characterName)}&worldname={Uri.EscapeDataString(world)}";
            var searchHtml = await httpClient.GetStringAsync(searchUrl, cancellationToken).ConfigureAwait(false);

            var profileMatch = ProfileLinkPattern().Match(searchHtml);
            if (!profileMatch.Success)
            {
                return null;
            }

            var profileUrl = $"https://na.finalfantasyxiv.com{profileMatch.Groups["path"].Value}";
            var profileHtml = await httpClient.GetStringAsync(profileUrl, cancellationToken).ConfigureAwait(false);

            // Lodestone's profile page shows "<Race> / <Clan>" in one block near the character's
            // name - matching on known race names directly rather than a specific CSS class,
            // since class names are the part most likely to have drifted from this guess.
            foreach (var race in KnownRaces)
            {
                if (profileHtml.Contains(race, StringComparison.Ordinal))
                {
                    return race;
                }
            }

            return null;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[Lodestone] race check failed for {characterName}@{world}: {exception.Message}");
            return null;
        }
    }

    [GeneratedRegex(@"href=""(?<path>/lodestone/character/\d+/)""")]
    private static partial Regex ProfileLinkPattern();
}
