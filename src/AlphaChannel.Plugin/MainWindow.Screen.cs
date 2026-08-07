using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace AlphaChannel.Plugin;

// Screen is a calibration tool — transform + clearer presets (Venues deferred from launch nav).
internal sealed partial class MainWindow
{
    private string presetNameInput = string.Empty;

    private void DrawScreenControls()
    {
        var engine = screenController.Engine;

        ImGui.TextUnformatted("Transform");
        ImGui.TextColored(MutedText, "Drag while looking at the in-world panel, or nudge with the sliders.");
        ImGui.Spacing();

        var position = engine.ScreenPosition;
        var yaw = engine.ScreenYaw;
        var scale = engine.ScreenScale;
        var changed = false;
        changed |= ImGui.DragFloat3("Position", ref position, 0.05f);
        changed |= ImGui.SliderAngle("Yaw", ref yaw);
        changed |= ImGui.SliderFloat("Scale", ref scale, VideoEngine.MinScreenScale, VideoEngine.MaxScreenScale);
        if (changed)
        {
            engine.SetScreenTransform(position, yaw, scale);
        }

        if (ImGui.Button("Recenter in front of me", new Vector2(-1, 34)))
        {
            engine.RecenterScreen();
        }

        ImGui.Spacing();
        ImGui.Dummy(new Vector2(0, 4));
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(origin, origin + new Vector2(width, 1f),
            ImGui.GetColorU32(BorderSubtle));
        ImGui.Dummy(new Vector2(width, 12f));

        ImGui.TextUnformatted("Presets");
        ImGui.TextColored(MutedText, "Save this spot for a venue, house, or FC room you revisit.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##presetName", "Name this spot…", ref presetNameInput, 48);

        using (ImRaii.Disabled(presetNameInput.Trim().Length == 0))
        {
            if (ImGui.Button("Save current position", new Vector2(-1, 34)))
            {
                var savePos = engine.ScreenPosition;
                Plugin.Cfg.ScreenPresets.Add(new ScreenPositionPreset
                {
                    Name = presetNameInput.Trim(),
                    X = savePos.X,
                    Y = savePos.Y,
                    Z = savePos.Z,
                    Yaw = engine.ScreenYaw,
                    Scale = engine.ScreenScale,
                });
                Plugin.Cfg.Save();
                presetNameInput = string.Empty;
            }
        }

        ImGui.Spacing();
        if (Plugin.Cfg.ScreenPresets.Count == 0)
        {
            DrawPlainEmpty("No presets yet — place the screen, then save it above.");
            return;
        }

        for (var index = 0; index < Plugin.Cfg.ScreenPresets.Count; index++)
        {
            var preset = Plugin.Cfg.ScreenPresets[index];
            ImGui.PushID(index);

            using (ImRaii.PushColor(ImGuiCol.ChildBg, CardBg))
            using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 10)))
            using (ImRaii.PushStyle(ImGuiStyleVar.ChildRounding, 10f))
            using (var row = ImRaii.Child("##presetRow", new Vector2(-1, 0), false,
                       PaddedChild | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar))
            {
                if (row)
                {
                    ImGui.TextUnformatted(preset.Name);
                    ImGui.TextColored(MutedText,
                        $"xyz {preset.X:0.0}, {preset.Y:0.0}, {preset.Z:0.0}  ·  scale {preset.Scale:0.00}");
                    ImGui.Spacing();
                    if (ImGui.Button("Load", new Vector2(90, 28)))
                    {
                        engine.SetScreenTransform(new Vector3(preset.X, preset.Y, preset.Z), preset.Yaw, preset.Scale);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Overwrite", new Vector2(100, 28)))
                    {
                        var pos = engine.ScreenPosition;
                        preset.X = pos.X;
                        preset.Y = pos.Y;
                        preset.Z = pos.Z;
                        preset.Yaw = engine.ScreenYaw;
                        preset.Scale = engine.ScreenScale;
                        Plugin.Cfg.Save();
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Delete", new Vector2(90, 28)))
                    {
                        Plugin.Cfg.ScreenPresets.RemoveAt(index);
                        Plugin.Cfg.Save();
                        ImGui.PopID();
                        break;
                    }
                }
            }

            ImGui.PopID();
            ImGui.Spacing();
        }
    }
}
