using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private string? browseVideoTopicFilter;
    private int browseVideoSortMode;
    private void DrawVideoGrid()
    {
        // Load once when the page is first opened.
        if (!browseVideoRequested)
        {
            browseVideoRequested = true;
            isLoadingBrowseVideos = true;

            _ = LoadBrowseVideosAsync();
        }

       

        // ---------------------------------------------------------
        // Topic filters
        // ---------------------------------------------------------

        ImGui.TextColored(
            Accent,
            "Topics");

        ImGui.SameLine(0f, 10f);

        // All
        var allSelected =
            browseVideoTopicFilter is null;

        using (ImRaii.PushColor(
            ImGuiCol.Button,
            allSelected ? Accent : CardBg)
            .Push(
                ImGuiCol.ButtonHovered,
                allSelected ? AccentHover : CardBgHover)
            .Push(
                ImGuiCol.ButtonActive,
                AccentActive))
        {
            if (ImGui.Button("All"))
            {
                browseVideoTopicFilter = null;
            }
        }


        // Loaded topic filters
        if (browseVideoResults is { Count: > 0 })
        {
            foreach (var topicName in browseVideoResults.Keys)
            {
                ImGui.SameLine(0f, 8f);

                var selected =
                    string.Equals(
                        browseVideoTopicFilter,
                        topicName,
                        StringComparison.Ordinal);

                using (ImRaii.PushColor(
                    ImGuiCol.Button,
                    selected ? Accent : CardBg)
                    .Push(
                        ImGuiCol.ButtonHovered,
                        selected ? AccentHover : CardBgHover)
                    .Push(
                        ImGuiCol.ButtonActive,
                        AccentActive))
                {
                    if (ImGui.Button(
                        $"{topicName}##browseFilter_{topicName}"))
                    {
                        browseVideoTopicFilter = topicName;
                    }
                }
            }
        }

        // ---------------------------------------------------------
        // Sort controls
        // ---------------------------------------------------------

        ImGui.Dummy(
            new Vector2(
                0f,
                8f));

        ImGui.TextColored(
            MutedText,
            "Sort:");

        ImGui.SameLine(0f, 10f);

        var trendingSelected =
            browseVideoSortMode == 0;

        using (ImRaii.PushColor(
            ImGuiCol.Button,
            trendingSelected ? Accent : CardBg)
            .Push(
                ImGuiCol.ButtonHovered,
                trendingSelected ? AccentHover : CardBgHover)
            .Push(
                ImGuiCol.ButtonActive,
                AccentActive))
        {
            if (ImGui.Button("Trending"))
            {
                browseVideoSortMode = 0;
            }
        }

        ImGui.SameLine(0f, 8f);

        var newestSelected =
            browseVideoSortMode == 1;

        using (ImRaii.PushColor(
            ImGuiCol.Button,
            newestSelected ? Accent : CardBg)
            .Push(
                ImGuiCol.ButtonHovered,
                newestSelected ? AccentHover : CardBgHover)
            .Push(
                ImGuiCol.ButtonActive,
                AccentActive))
        {
            if (ImGui.Button("Newest"))
            {
                browseVideoSortMode = 1;
            }
        }

        ImGui.SameLine(0f, 8f);

        var viewedSelected =
            browseVideoSortMode == 2;

        using (ImRaii.PushColor(
            ImGuiCol.Button,
            viewedSelected ? Accent : CardBg)
            .Push(
                ImGuiCol.ButtonHovered,
                viewedSelected ? AccentHover : CardBgHover)
            .Push(
                ImGuiCol.ButtonActive,
                AccentActive))
        {
            if (ImGui.Button("Most Viewed"))
            {
                browseVideoSortMode = 2;
            }
        }

        // ---------------------------------------------------------
        // Refresh icon — far right, no background
        // ---------------------------------------------------------

        ImGui.SameLine();

        ImGui.SetCursorPosX(
            ImGui.GetWindowContentRegionMax().X - 20f);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(
                AccentHover,
                FontAwesomeIcon.Sync.ToIconString());
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(
                ImGuiMouseCursor.Hand);

            ImGui.SetTooltip(
                "Refresh Browse Videos");
        }

        if (ImGui.IsItemClicked())
        {
            browseVideoTopicFilter = null;
            browseVideoResults = null;
            isLoadingBrowseVideos = true;

            _ = LoadBrowseVideosAsync(true);
        }

        ImGui.Dummy(
            new Vector2(0f, 15f));

        // Scrollable content area
        using var child = ImRaii.Child(
            "##browseVideoContent",
            new Vector2(
                0f,
                -1f),
            false);

        if (!child)
        {
            return;
        }

        if (isLoadingBrowseVideos &&
       (browseVideoResults is null ||
        browseVideoResults.Count == 0))
        {
            ImGui.TextColored(
                MutedText,
                "Loading videos...");

            return;
        }

        if (browseVideoResults is null ||
            browseVideoResults.Count == 0)
        {
            ImGui.TextColored(
                MutedText,
                "No videos loaded.");

            return;
        }

        if (isLoadingBrowseVideos)
        {
            ImGui.TextColored(
                MutedText,
                $"Loading more topics... {browseVideoResults!.Count}/8");

            ImGui.Dummy(
                new Vector2(
                    0f,
                    10f));
        }

        const int columns = 5;
        const float gap = 10f;
        const float rowGap = 16f;
        const float cardHeight = 204f;

        var contentWidth =
            ImGui.GetContentRegionAvail().X;

        var cardWidth =
            (contentWidth - gap * (columns - 1)) /
            columns;

        var topicIndex = 0;

        var visibleTopics =
            browseVideoTopicFilter is null
                ? browseVideoResults
                : browseVideoResults
                    .Where(x =>
                        string.Equals(
                            x.Key,
                            browseVideoTopicFilter,
                            StringComparison.Ordinal))
                    .ToDictionary(
                        x => x.Key,
                        x => x.Value);

        foreach (var topic in visibleTopics)
        {
            if (topicIndex > 0)
            {
                ImGui.Dummy(
                    new Vector2(
                        0f,
                        rowGap));
            }

            // Topic heading
            var topicIcon =
                topic.Key switch
                {
                    "Gaming" => FontAwesomeIcon.Gamepad,
                    "MMORPG" => FontAwesomeIcon.Users,
                    "Final Fantasy" => FontAwesomeIcon.Magic,
                    "Anime" => FontAwesomeIcon.Star,
                    "Movies" => FontAwesomeIcon.Film,
                    "TV Shows" => FontAwesomeIcon.Tv,
                    "Music" => FontAwesomeIcon.Music,
                    "Memes" => FontAwesomeIcon.Grin,
                    "Wildlife" => FontAwesomeIcon.Paw,
                    "Architecture" => FontAwesomeIcon.Building,
                    "Science" => FontAwesomeIcon.Flask,
                    "Space" => FontAwesomeIcon.Rocket,
                    "History" => FontAwesomeIcon.Landmark,
                    "Technology" => FontAwesomeIcon.Microchip,
                    "Pets" => FontAwesomeIcon.Paw,
                    "Food" => FontAwesomeIcon.Utensils,
                    "Travel" => FontAwesomeIcon.Plane,
                    "Cars" => FontAwesomeIcon.Car,
                    "Sports" => FontAwesomeIcon.Futbol,
                    _ => FontAwesomeIcon.PlayCircle
                };

            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(
                    AccentHover,
                    topicIcon.ToIconString());
            }

            ImGui.SameLine(0f, 8f);

            ImGui.SetWindowFontScale(1.08f);

            ImGui.TextColored(
                Vector4.One,
                topic.Key);

            ImGui.SetWindowFontScale(1f);

            ImGui.Dummy(
                new Vector2(
                    0f,
                    8f));
            var videos =
    browseVideoSortMode switch
    {
        1 => topic.Value
            .OrderByDescending(
                x => x.UploadDate ?? DateTime.MinValue)
            .ToList(),

        2 => topic.Value
            .OrderByDescending(
                x => x.ViewCount ?? 0)
            .ToList(),

        _ => topic.Value
            .OrderByDescending(GetTrendingScore)
            .ToList()
    };

            // All topics = 5 videos per topic.
            // Single-topic filter = up to 15 videos.
            var maxVideos =
                browseVideoTopicFilter is null
                    ? 5
                    : 15;

            var visibleCount =
                Math.Min(
                    maxVideos,
                    videos.Count);

            const float videoRowGap = 16f;

            for (var index = 0;
                 index < visibleCount;
                 index++)
            {
                if (index > 0)
                {
                    if (index % columns == 0)
                    {
                        // Start a new row after every 5 cards.
                        ImGui.Dummy(
                            new Vector2(
                                0f,
                                videoRowGap));
                    }
                    else
                    {
                        ImGui.SameLine(
                            0f,
                            gap);
                    }
                }

                ImGui.PushID(
                    $"browse_{topicIndex}_{index}");

                DrawHomeYouTubeCard(
                    videos[index],
                    cardWidth,
                    cardHeight);

                ImGui.PopID();
            }

            topicIndex++;
        }
    }
}