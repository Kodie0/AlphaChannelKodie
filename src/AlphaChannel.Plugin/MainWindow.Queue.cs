using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private const float QueueThumbnailHeight = 40f;

    private void DrawQueue()
    {
        var count = queue.Entries.Count;

        // ---------------------------------------------------------
        // Header
        // ---------------------------------------------------------

        ImGui.SetWindowFontScale(1.15f);

        ImGui.TextColored(
            Vector4.One,
            "Queue");

        ImGui.SetWindowFontScale(1f);

        ImGui.SameLine(0f, 6f);

        ImGui.SetWindowFontScale(0.72f);

        ImGui.TextColored(
            MutedText,
            count == 1
                ? "1 video"
                : $"{count} videos");

        ImGui.SetWindowFontScale(1f);

        // Clear queue button aligned right.
        if (count > 0)
        {
            var clearSize = new Vector2(112f, 32f);

            ImGui.SameLine(
                ImGui.GetContentRegionMax().X -
                clearSize.X);

            using (ImRaii.PushStyle(
                ImGuiStyleVar.FrameRounding,
                8f))
            using (ImRaii.PushColor(
                ImGuiCol.Button,
                new Vector4(0.045f, 0.055f, 0.09f, 1f))
                .Push(
                    ImGuiCol.ButtonHovered,
                    new Vector4(0.065f, 0.08f, 0.125f, 1f))
                .Push(
                    ImGuiCol.ButtonActive,
                    new Vector4(0.075f, 0.09f, 0.14f, 1f)))
            {
                var buttonPos =
                    ImGui.GetCursorScreenPos();

                if (ImGui.Button(
                    "##clearQueue",
                    clearSize))
                {
                    foreach (var entry in queue.Entries.ToList())
                    {
                        queue.Remove(entry);
                    }
                }

                DrawPlayerActionButtonContent(
                    buttonPos,
                    clearSize,
                    FontAwesomeIcon.Trash,
                    "Clear queue",
                    MutedText);
            }
        }

        ImGui.Dummy(new Vector2(0f, 14f));

        // ---------------------------------------------------------
        // Empty state
        // ---------------------------------------------------------

        if (count == 0)
        {
            ImGui.SetWindowFontScale(0.9f);

            ImGui.TextColored(
                MutedText,
                "Nothing queued yet.");

            ImGui.SetWindowFontScale(1f);

            ImGui.Dummy(new Vector2(0f, 4f));

            ImGui.SetWindowFontScale(0.78f);

            ImGui.TextColored(
                MutedText,
                "Add videos from Link, YouTube, Twitch, or Discover.");

            ImGui.SetWindowFontScale(1f);

            return;
        }

        // ---------------------------------------------------------
        // Scrollable queue list
        // ---------------------------------------------------------

        var queueListHeight = MathF.Max(
            120f,
            ImGui.GetContentRegionAvail().Y - 4f);

        using var child = ImRaii.Child(
            "##queueList",
            new Vector2(-1f, queueListHeight),
            false,
            ImGuiWindowFlags.None);

        if (!child)
        {
            return;
        }

        for (var index = 0; index < queue.Entries.Count; index++)
        {
            var entry = queue.Entries[index];

            ImGui.PushID(index);

            const float rowHeight = 66f;

            using (ImRaii.PushStyle(
                ImGuiStyleVar.ChildRounding,
                8f))
            using (ImRaii.PushColor(
                ImGuiCol.ChildBg,
                new Vector4(0.045f, 0.06f, 0.10f, 1f)))
            using (var row = ImRaii.Child(
                $"##queue_{entry.Id}",
                new Vector2(-10f, rowHeight),
                false,
                ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse))
            {
                if (row)
                {
                    var rowOrigin =
                        ImGui.GetCursorScreenPos();

                    var rowWidth =
                        ImGui.GetWindowWidth();

                    var drawList =
                        ImGui.GetWindowDrawList();

                    // -------------------------------------------------
                    // Thumbnail
                    // -------------------------------------------------

                    const float thumbWidth = 118f;
                    const float thumbHeight = rowHeight;

                    var thumbnail =
                        thumbnails.Get(entry.ThumbnailUrl);

                    if (thumbnail is not null)
                    {
                        drawList.AddImageRounded(
                            thumbnail.Handle,
                            rowOrigin,
                            rowOrigin + new Vector2(
                                thumbWidth,
                                thumbHeight),
                            Vector2.Zero,
                            Vector2.One,
                            uint.MaxValue,
                            8f);
                    }

                    // -------------------------------------------------
                    // Text
                    // -------------------------------------------------

                    var contentX =
                        rowOrigin.X +
                        thumbWidth +
                        12f;

                    // Keep this much space free for all right-side
                    // controls, regardless of which arrows are visible.
                    const float controlsWidth = 150f;

                    var textWidth =
                        MathF.Max(
                            80f,
                            rowWidth -
                            thumbWidth -
                            controlsWidth -
                            28f);

                    ImGui.SetCursorScreenPos(
                        new Vector2(
                            contentX,
                            rowOrigin.Y + 9f));

                    ImGui.PushTextWrapPos(
                        contentX + textWidth);

                    ImGui.TextColored(
                        Vector4.One,
                        entry.Title);

                    ImGui.PopTextWrapPos();

                    var meta =
                        $"{entry.Source}  •  " +
                        (entry.Duration is { } duration
                            ? FormatTime(
                                (float)duration.TotalSeconds)
                            : "Live");

                    ImGui.SetCursorScreenPos(
                        new Vector2(
                            contentX,
                            rowOrigin.Y + 37f));

                    ImGui.SetWindowFontScale(0.88f);

                    ImGui.TextColored(
                        MutedText,
                        meta);

                    ImGui.SetWindowFontScale(1f);

                    // -------------------------------------------------
                    // Fixed right-side controls
                    // -------------------------------------------------

                    // Everything is positioned from the right edge.
                    // This means missing Up/Down arrows never move the
                    // dots or any of the other controls.

                    const float rightPadding = 12f;
                    const float iconSize = 22f;
                    const float iconGap = 4f;

                    var controlsY =
                        rowOrigin.Y +
                        (rowHeight - iconSize) * 0.5f;

                    // Vertical dots are ALWAYS fixed to the far right.
                    var menuX =
                        rowOrigin.X +
                        rowWidth -
                        rightPadding -
                        iconSize;

                    // Down arrow always owns this slot.
                    var downX =
                        menuX -
                        iconGap -
                        iconSize;

                    // Up arrow always owns this slot.
                    var upX =
                        downX -
                        iconGap -
                        iconSize;

                    // -------------------------------------------------
                    // Menu dots
                    // -------------------------------------------------

                    ImGui.SetCursorScreenPos(
                        new Vector2(
                            menuX,
                            controlsY));

                    if (DrawQueueGhostIcon(
                        $"##queueMenuButton_{entry.Id}",
                        FontAwesomeIcon.EllipsisV))
                    {
                        ImGui.OpenPopup(
                            $"queueMenu_{entry.Id}");
                    }

                    if (ImGui.BeginPopup(
                        $"queueMenu_{entry.Id}"))
                    {
                        if (ImGui.MenuItem("Remove"))
                        {
                            queue.Remove(entry);
                        }

                        ImGui.EndPopup();
                    }

                    // -------------------------------------------------
                    // Down
                    // -------------------------------------------------

                    if (index < queue.Entries.Count - 1)
                    {
                        ImGui.SetCursorScreenPos(
                            new Vector2(
                                downX,
                                controlsY));

                        if (DrawQueueGhostIcon(
                            $"##queueDown_{entry.Id}",
                            FontAwesomeIcon.ChevronDown))
                        {
                            queue.Reorder(
                                index,
                                index + 1);
                        }
                    }

                    // -------------------------------------------------
                    // Up
                    // -------------------------------------------------

                    if (index > 0)
                    {
                        ImGui.SetCursorScreenPos(
                            new Vector2(
                                upX,
                                controlsY));

                        if (DrawQueueGhostIcon(
                            $"##queueUp_{entry.Id}",
                            FontAwesomeIcon.ChevronUp))
                        {
                            queue.Reorder(
                                index,
                                index - 1);
                        }
                    }

                    // -------------------------------------------------
                    // Queue position / Up Next badge
                    // -------------------------------------------------

                    var pillText =
                        index == 0
                            ? "Up Next"
                            : $"{index + 1}";

                    var pillWidth =
                        index == 0
                            ? 58f
                            : 28f;

                    var pillSize =
                        new Vector2(
                            pillWidth,
                            22f);

                    // The badge ends just before the Up-arrow slot.
                    var pillX =
                        upX -
                        8f -
                        pillWidth;

                    var pillY =
                        rowOrigin.Y +
                        (rowHeight - pillSize.Y) * 0.5f;

                    var pillPos =
                        new Vector2(
                            pillX,
                            pillY);

                    drawList.AddRectFilled(
                        pillPos,
                        pillPos + pillSize,
                        ImGui.GetColorU32(
                            new Vector4(
                                0.06f,
                                0.075f,
                                0.125f,
                                1f)),
                        6f);

                    var pillTextSize =
                        ImGui.CalcTextSize(
                            pillText);

                    drawList.AddText(
                        pillPos +
                        new Vector2(
                            (pillSize.X - pillTextSize.X) * 0.5f,
                            (pillSize.Y - pillTextSize.Y) * 0.5f),
                        ImGui.GetColorU32(
                            index == 0
                                ? Accent
                                : MutedText),
                        pillText);
                }
            }

            ImGui.PopID();

            ImGui.Dummy(
                new Vector2(0f, 8f));
        }
    }

    private static bool DrawQueueGhostIcon(
        string id,
        FontAwesomeIcon icon)
    {
        const float size = 22f;

        var origin = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(
            id,
            new Vector2(size, size));

        var hovered = ImGui.IsItemHovered();

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var glyph = icon.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);

            ImGui.GetWindowDrawList().AddText(
                origin + new Vector2(
                    (size - glyphSize.X) * 0.5f,
                    (size - glyphSize.Y) * 0.5f),
                ImGui.GetColorU32(
                    hovered
                        ? Vector4.One
                        : MutedText),
                glyph);
        }

        return clicked;
    }
}