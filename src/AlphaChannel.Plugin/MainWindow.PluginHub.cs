using AlphaChannel.Contracts;
using AlphaChannel.Plugin.Auth;
using AlphaChannel.Plugin.PluginHub;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Plugin Hub: "what does my friend actually run?" - auto-detected from each client's own
// IDalamudPluginInterface.InstalledPlugins (see InstalledPluginsReader), never a manually maintained
// directory. Friends-only, same posture as Alpha Chat/Activity - see PluginHubService server-side.
internal sealed partial class MainWindow
{
    private bool pluginSyncInFlight;
    private string? lastPluginSyncToken;
    private InstalledPluginDto[] myEnabledPlugins = [];
    private bool myPluginsDirty = true;
    private string pluginHubSearch = string.Empty;
    private string? pluginHubTabCompletion;

    private string? selectedFriendAccountId;
    private string? selectedFriendDisplayName;
    private InstalledPluginDto[] friendPlugins = [];
    private bool friendPluginsLoading;
    private string? friendPluginsError;

    private void DrawPluginHub()
    {
        if (CurrentSession is not { } session)
        {
            DrawPlainEmpty("See friends' Dalamud plugins after you sign in.", "Open Settings",
                () => currentPage = HomePage.Settings);
            return;
        }

        ImGui.TextColored(MutedText,
            "Enabled plugins only. Click a name to open or close it — search, Tab to finish, Enter to toggle.");
        ImGui.Spacing();

        if (myPluginsDirty)
        {
            myEnabledPlugins = InstalledPluginsReader.ReadCurrent();
            myPluginsDirty = false;
        }

        using (ImRaii.Disabled(pluginSyncInFlight))
        {
            if (ImGui.SmallButton(pluginSyncInFlight ? "Syncing..." : "Refresh & sync"))
            {
                SyncMyPlugins(session.Token);
            }
        }

        ImGui.SameLine();
        ImGui.TextColored(MutedText, $"{myEnabledPlugins.Length} enabled");

        ImGui.Spacing();
        DrawPluginHubSearch();

        var mineFiltered = FilterPlugins(myEnabledPlugins, pluginHubSearch);
        var friendFiltered = FilterPlugins(friendPlugins, pluginHubSearch);

        ImGui.Spacing();
        SectionHeader("Yours");
        if (myEnabledPlugins.Length == 0)
        {
            ImGui.TextDisabled("No other plugins enabled right now.");
        }
        else if (mineFiltered.Length == 0)
        {
            ImGui.TextDisabled("Nothing matches that search.");
        }
        else
        {
            DrawCompactPluginGrid(mineFiltered, "##myPlugins");
        }

        ImGui.Spacing();

        if (friends.Length == 0)
        {
            ImGui.TextDisabled("Add some friends first - their plugin lists show up here.");
            return;
        }

        SectionHeader("Friends");
        DrawCompactFriendPicker(session);

        if (selectedFriendAccountId is null)
        {
            return;
        }

        ImGui.Spacing();
        SectionHeader($"{selectedFriendDisplayName}'s plugins");

        if (friendPluginsLoading)
        {
            ImGui.TextDisabled("Loading...");
        }
        else if (friendPluginsError is { Length: > 0 } error)
        {
            ImGui.TextColored(Danger, error);
        }
        else if (friendPlugins.Length == 0)
        {
            ImGui.TextDisabled("Nothing shared yet - they haven't opened the Plugin Hub, or don't have any.");
        }
        else if (friendFiltered.Length == 0)
        {
            ImGui.TextDisabled("Nothing matches that search.");
        }
        else
        {
            DrawCompactPluginGrid(friendFiltered, "##friendPlugins");
        }
    }

    private void DrawPluginHubSearch()
    {
        // Search pool: yours first, then the selected friend's list — Tab completes against the
        // best match in that order so typing "gla" finishes as "Glamourer" when you have it.
        var pool = selectedFriendAccountId is null
            ? myEnabledPlugins
            : myEnabledPlugins.Concat(friendPlugins)
                .GroupBy(p => p.InternalName, StringComparer.Ordinal)
                .Select(g => g.First())
                .ToArray();

        var suggestion = FindSearchCompletion(pool, pluginHubSearch);
        pluginHubTabCompletion = suggestion is { } match
            && pluginHubSearch.Length > 0
            && !string.Equals(pluginHubSearch, match.Name, StringComparison.OrdinalIgnoreCase)
            ? match.Name
            : null;

        ImGui.SetNextItemWidth(-1);
        var submitted = ImGui.InputTextWithHint("##pluginHubSearch", "Search plugins…", ref pluginHubSearch, 64,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackCompletion,
            PluginHubSearchCallback);

        if (pluginHubTabCompletion is { } hint)
        {
            ImGui.TextColored(MutedText, $"Tab → {hint}");
        }

        if (submitted && suggestion is { } open
            && InstalledPluginsReader.CanOpen(open.InternalName))
        {
            InstalledPluginsReader.TryToggle(open.InternalName);
        }
    }

    private int PluginHubSearchCallback(ImGuiInputTextCallbackDataPtr data)
    {
        if (data.EventFlag != ImGuiInputTextFlags.CallbackCompletion || pluginHubTabCompletion is not { } fill)
        {
            return 0;
        }

        data.DeleteChars(0, data.BufTextLen);
        data.InsertChars(0, fill);
        data.CursorPos = data.BufTextLen;
        data.SelectionStart = data.CursorPos;
        data.SelectionEnd = data.CursorPos;
        return 0;
    }

    // Prefer a case-insensitive prefix match ("gla" → Glamourer); fall back to the earliest
    // substring hit so a mid-name query still finishes something useful.
    private static InstalledPluginDto? FindSearchCompletion(InstalledPluginDto[] pool, string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0 || pool.Length == 0)
        {
            return null;
        }

        var prefix = pool
            .Where(p => p.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                        || p.InternalName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name.Length)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (prefix is not null)
        {
            return prefix;
        }

        return pool
            .Where(p => p.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                        || p.InternalName.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name.Length)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static InstalledPluginDto[] FilterPlugins(InstalledPluginDto[] plugins, string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return plugins;
        }

        return plugins
            .Where(p => p.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                        || p.InternalName.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    // Two-column chip grid in a short scroll pane so a 40+ plugin install doesn't dominate the page.
    private void DrawCompactPluginGrid(InstalledPluginDto[] plugins, string childId)
    {
        const float maxHeight = 148f;
        var lineH = ImGui.GetTextLineHeightWithSpacing();
        var rows = (plugins.Length + 1) / 2;
        var needed = MathF.Min(maxHeight, MathF.Max(rows * lineH + 8f, lineH + 8f));

        using (var child = ImRaii.Child(childId, new Vector2(0, needed), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (!child)
            {
                return;
            }

            var avail = ImGui.GetContentRegionAvail().X;
            var colWidth = (avail - 8f) * 0.5f;
            for (var i = 0; i < plugins.Length; i++)
            {
                if (i % 2 == 1)
                {
                    ImGui.SameLine(0, 8f);
                }

                DrawPluginChip(plugins[i], colWidth);
            }
        }
    }

    private void DrawPluginChip(InstalledPluginDto plugin, float width)
    {
        ImGui.PushID(plugin.InternalName);
        var canOpen = InstalledPluginsReader.CanOpen(plugin.InternalName);
        var label = plugin.Name;
        var size = new Vector2(MathF.Max(width, 80f), ImGui.GetTextLineHeightWithSpacing());
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var clicked = ImGui.InvisibleButton("##chip", size);
        var hovered = ImGui.IsItemHovered();
        var opened = canOpen && InstalledPluginsReader.IsOpenedByHub(plugin.InternalName);

        var fill = opened
            ? new Vector4(Accent.X, Accent.Y, Accent.Z, hovered ? 0.28f : 0.18f)
            : (hovered ? CardBgHover : CardBg);
        drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(fill), 6f);
        if (opened)
        {
            drawList.AddRect(origin, origin + size,
                ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.7f)), 6f,
                ImDrawFlags.None, 1.2f);
        }

        var textColor = canOpen ? (hovered || opened ? AccentHover : Vector4.One) : MutedText;
        drawList.AddText(origin + new Vector2(8, (size.Y - ImGui.GetTextLineHeight()) * 0.5f),
            ImGui.GetColorU32(textColor), TruncateToWidth(label, size.X - 16f));

        if (hovered)
        {
            ImGui.SetTooltip(canOpen
                ? $"{plugin.Name}\n{plugin.Version}\nClick to {(opened ? "close" : "open")}"
                : $"{plugin.Name}\n{plugin.Version}\nNo openable UI (or not installed here)");
        }

        if (clicked && canOpen)
        {
            InstalledPluginsReader.TryToggle(plugin.InternalName);
        }

        ImGui.PopID();
    }

    private void DrawCompactFriendPicker(CharacterSession session)
    {
        const float maxHeight = 100f;
        var lineH = ImGui.GetTextLineHeightWithSpacing();
        var needed = MathF.Min(maxHeight, friends.Length * lineH + 4f);
        using (var child = ImRaii.Child("##hubFriends", new Vector2(0, needed), false, ImGuiWindowFlags.NoScrollbar))
        {
            if (!child)
            {
                return;
            }

            foreach (var friend in friends)
            {
                ImGui.PushID(friend.AccountId);
                var selected = selectedFriendAccountId == friend.AccountId;
                if (ImGui.Selectable(friend.DisplayName, selected))
                {
                    SelectFriendForPluginHub(session, friend.AccountId, friend.DisplayName);
                }

                ImGui.PopID();
            }
        }
    }

    private static string TruncateToWidth(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        for (var len = text.Length - 1; len > 0; len--)
        {
            var candidate = text[..len] + ellipsis;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
            {
                return candidate;
            }
        }

        return ellipsis;
    }

    // Called once per newly-established session from DrawSidebar (see its own comment on why that
    // runs every frame regardless of which page is open), plus on demand via the button above -
    // enabled plugins can change mid-session and there's no live hook, so refresh re-reads IsLoaded.
    private void SyncMyPlugins(string bearerToken)
    {
        pluginSyncInFlight = true;
        myEnabledPlugins = InstalledPluginsReader.ReadCurrent();
        myPluginsDirty = false;
        var installed = myEnabledPlugins;
        _ = Task.Run(async () =>
        {
            await pluginHubClient.SyncAsync(bearerToken, installed);
            pluginSyncInFlight = false;
        });
    }

    private void SelectFriendForPluginHub(CharacterSession session, string accountId, string displayName)
    {
        selectedFriendAccountId = accountId;
        selectedFriendDisplayName = displayName;
        friendPlugins = [];
        friendPluginsLoading = true;
        friendPluginsError = null;
        var token = session.Token;
        _ = Task.Run(async () =>
        {
            var result = await pluginHubClient.GetFriendPluginsAsync(token, accountId);
            friendPlugins = result ?? [];
            friendPluginsError = result is null ? "Couldn't load their plugin list." : null;
            friendPluginsLoading = false;
        });
    }
}
