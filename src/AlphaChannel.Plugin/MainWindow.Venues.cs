using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Venues: persistent hangout spaces - a named, saved screen placement (see Venue's server-side doc
// comment) shareable with friends. The purely-local equivalent (MainWindow.Screen.cs's presets) is
// still there for a quick personal "remember this spot" with no server round-trip; a Venue is the
// same idea made visible to friends, anchored to the zone it was recorded in via TerritoryTypeId.
internal sealed partial class MainWindow
{
    private bool myVenuesDirty = true;
    private bool myVenuesLoading;
    private VenueDto[] myVenues = [];
    private string venueNameInput = string.Empty;
    private bool venueSaving;
    private string? venueError;

    private string? selectedVenueFriendAccountId;
    private string? selectedVenueFriendDisplayName;
    private VenueDto[] friendVenues = [];
    private bool friendVenuesLoading;
    private string? friendVenuesError;

    private void DrawVenues()
    {
        if (CurrentSession is not { } session)
        {
            DrawPlainEmpty("Named screen spots sync once you're signed in.", "Open Settings",
                () => currentPage = HomePage.Settings);
            return;
        }

        if (myVenuesDirty && !myVenuesLoading)
        {
            RefreshMyVenues(session.Token);
        }

        var currentZone = (int)Plugin.ClientState.TerritoryType;

        SectionHeader("Save this spot");
        ImGui.TextColored(MutedText, "Saves the screen's current position/angle/size here, tagged to this zone.");
        ImGui.SetNextItemWidth(-100f);
        ImGui.InputTextWithHint("##venueName", "Venue name", ref venueNameInput, 48);
        ImGui.SameLine();
        using (ImRaii.Disabled(venueSaving || venueNameInput.Trim().Length == 0))
        {
            if (ImGui.Button(venueSaving ? "Saving..." : "Save"))
            {
                SaveVenue(session, currentZone);
            }
        }

        if (venueError is { Length: > 0 } saveError)
        {
            ImGui.TextColored(Danger, saveError);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        SectionHeader($"My venues ({myVenues.Length})");
        if (myVenues.Length == 0)
        {
            ImGui.TextDisabled(myVenuesLoading ? "Loading..." : "No venues saved yet.");
        }
        else
        {
            foreach (var venue in myVenues)
            {
                DrawVenueRow(venue, currentZone, isMine: true, session);
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();

        SectionHeader("Friends' venues");
        if (friends.Length == 0)
        {
            ImGui.TextDisabled("Add some friends first - their saved venues show up here.");
            return;
        }

        foreach (var friend in friends)
        {
            ImGui.PushID(friend.AccountId);
            var selected = selectedVenueFriendAccountId == friend.AccountId;
            if (ImGui.Selectable(friend.DisplayName, selected))
            {
                SelectFriendForVenues(session, friend.AccountId, friend.DisplayName);
            }

            ImGui.PopID();
        }

        if (selectedVenueFriendAccountId is null)
        {
            return;
        }

        ImGui.Spacing();
        SectionHeader($"{selectedVenueFriendDisplayName}'s venues");

        if (friendVenuesLoading)
        {
            ImGui.TextDisabled("Loading...");
        }
        else if (friendVenuesError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }
        else if (friendVenues.Length == 0)
        {
            ImGui.TextDisabled("No venues shared yet.");
        }
        else
        {
            foreach (var venue in friendVenues)
            {
                DrawVenueRow(venue, currentZone, isMine: false, session);
            }
        }
    }

    private void DrawVenueRow(VenueDto venue, int currentZone, bool isMine, CharacterSession session)
    {
        ImGui.PushID(venue.Id);
        ImGui.Text(venue.Name);

        var sameZone = venue.TerritoryTypeId == currentZone;
        if (!sameZone)
        {
            ImGui.SameLine();
            ImGui.TextColored(MutedText, "(different zone)");
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!sameZone))
        {
            if (ImGui.SmallButton("Load"))
            {
                screenController.Engine.SetScreenTransform(new Vector3(venue.ScreenX, venue.ScreenY, venue.ScreenZ), venue.ScreenYaw, venue.ScreenScale);
            }
        }

        if (isMine)
        {
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Danger))
            {
                if (ImGui.SmallButton("Delete"))
                {
                    var token = session.Token;
                    var venueId = venue.Id;
                    _ = Task.Run(async () => { await venuesClient.DeleteAsync(token, venueId); myVenuesDirty = true; });
                    myVenues = myVenues.Where(v => v.Id != venue.Id).ToArray();
                }
            }
        }

        ImGui.PopID();
    }

    private void SaveVenue(CharacterSession session, int territoryTypeId)
    {
        var engine = screenController.Engine;
        var name = venueNameInput.Trim();
        var token = session.Token;
        var request = new CreateVenueRequest(name, territoryTypeId,
            engine.ScreenPosition.X, engine.ScreenPosition.Y, engine.ScreenPosition.Z, engine.ScreenYaw, engine.ScreenScale);

        venueSaving = true;
        venueError = null;
        _ = Task.Run(async () =>
        {
            var created = await venuesClient.CreateAsync(token, request);
            venueSaving = false;
            if (created is null)
            {
                venueError = "Couldn't save that venue - you may have hit the 50-venue limit.";
                return;
            }

            venueNameInput = string.Empty;
            myVenuesDirty = true;
        });
    }

    private void RefreshMyVenues(string bearerToken)
    {
        myVenuesDirty = false;
        myVenuesLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                myVenues = await venuesClient.GetMineAsync(bearerToken) ?? [];
            }
            finally
            {
                myVenuesLoading = false;
            }
        });
    }

    private void SelectFriendForVenues(CharacterSession session, string accountId, string displayName)
    {
        selectedVenueFriendAccountId = accountId;
        selectedVenueFriendDisplayName = displayName;
        friendVenues = [];
        friendVenuesLoading = true;
        friendVenuesError = null;
        var token = session.Token;
        _ = Task.Run(async () =>
        {
            var result = await venuesClient.GetFriendVenuesAsync(token, accountId);
            friendVenues = result ?? [];
            friendVenuesError = result is null ? "Couldn't load their venues." : null;
            friendVenuesLoading = false;
        });
    }
}
