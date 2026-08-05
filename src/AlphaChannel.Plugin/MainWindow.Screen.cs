using AlphaChannel.Plugin.Video;
using Dalamud.Bindings.ImGui;

namespace AlphaChannel.Plugin;

internal sealed partial class MainWindow
{
    private string presetNameInput = string.Empty;

    private void DrawScreenControls()
    {
        var engine = screenController.Engine;
        ImGui.Text("Screen");
        if (ImGui.Button("Recenter"))
        {
            engine.RecenterScreen();
        }

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

        ImGui.SetNextItemWidth(-70f);
        ImGui.InputTextWithHint("##presetName", "Preset name", ref presetNameInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Save") && presetNameInput.Length > 0)
        {
            Plugin.Cfg.ScreenPresets.Add(new ScreenPositionPreset
            {
                Name = presetNameInput.Trim(),
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Yaw = yaw,
                Scale = scale,
            });
            Plugin.Cfg.Save();
            presetNameInput = string.Empty;
        }

        for (var index = 0; index < Plugin.Cfg.ScreenPresets.Count; index++)
        {
            var preset = Plugin.Cfg.ScreenPresets[index];
            ImGui.PushID(index);
            ImGui.Text(preset.Name);
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
