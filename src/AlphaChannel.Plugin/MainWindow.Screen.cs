using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;

namespace AlphaChannel.Plugin;

// Screen is a calibration tool — dense sliders and a preset list, no media-stage chrome.
internal sealed partial class MainWindow
{
    private string presetNameInput = string.Empty;

    private void DrawScreenControls()
    {
        var engine = screenController.Engine;

        ImGui.TextUnformatted("Transform");
        ImGui.TextColored(MutedText, "Drag while looking at the in-world panel.");
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

        if (ImGui.Button("Recenter in front of me", new Vector2(-1, 32)))
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
        ImGui.TextColored(MutedText, "Named transforms for places you revisit.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-70f);
        ImGui.InputTextWithHint("##presetName", "Preset name", ref presetNameInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Save") && presetNameInput.Length > 0)
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

        if (Plugin.Cfg.ScreenPresets.Count == 0)
        {
            DrawPlainEmpty("No presets yet.");
            return;
        }

        ImGui.Spacing();
        for (var index = 0; index < Plugin.Cfg.ScreenPresets.Count; index++)
        {
            var preset = Plugin.Cfg.ScreenPresets[index];
            ImGui.PushID(index);
            ImGui.AlignTextToFramePadding();
            ImGui.BulletText(preset.Name);
            ImGui.SameLine();
            if (ImGui.SmallButton("Load"))
            {
                engine.SetScreenTransform(new Vector3(preset.X, preset.Y, preset.Z), preset.Yaw, preset.Scale);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Delete"))
            {
                Plugin.Cfg.ScreenPresets.RemoveAt(index);
                Plugin.Cfg.Save();
            }

            ImGui.PopID();
        }
    }
}
