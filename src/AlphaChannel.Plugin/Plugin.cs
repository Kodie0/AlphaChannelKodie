using AlphaChannel.Plugin.Video;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AlphaChannel.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal static Configuration Cfg { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("AlphaChannel");
    private readonly ScreenController screenController;
    private readonly VideoPlayer video;
    private readonly AetherStreamQueue queue;
    private readonly StreamClient stream;
    private readonly MainWindow mainWindow;

    public string Name => "AlphaChannel";

    public Plugin()
    {
        Cfg = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Cfg.Initialize(PluginInterface);

        // VideoEngine's own constructor calls DxHandler.Initialise, matching the original
        // Aetherphone ordering - no separate call needed here.
        screenController = new ScreenController(() => true);
        video = new VideoPlayer(screenController.Engine);
        queue = new AetherStreamQueue(video);
        stream = new StreamClient(Cfg, () => Cfg.CharacterDisplayNames.GetValueOrDefault(ReadLocalContentId()));
        stream.OnState += OnRemoteState;
        stream.OnRenameRequired += OnRenameRequired;
        stream.Start();

        mainWindow = new MainWindow(screenController, video, queue, stream);
        windowSystem.AddWindow(mainWindow);

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        CommandManager.AddHandler("/achannel", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the AlphaChannel window.",
        });
    }

    private void OnCommand(string command, string arguments) => ToggleMainWindow();

    private void ToggleMainWindow() => mainWindow.IsOpen = !mainWindow.IsOpen;

    private void OnFrameworkUpdate(IFramework framework)
    {
        screenController.OnFrameworkUpdate();
        queue.OnFrameworkUpdate();
        EnsureCharacterHasName();
        mainWindow.CurrentDisplayName = Cfg.CharacterDisplayNames.GetValueOrDefault(ReadLocalContentId());

        // Hosting: push the local queue's current state out to the relay every tick it changes
        // meaningfully - PublishStateAsync itself is cheap to call repeatedly (a JSON send), the
        // server is what dedupes/broadcasts, so no local diff-check is needed for a v1.
        if (queue.Current is { } current && screenController.Engine.IsActive)
        {
            var (position, _, paused) = video.GetProgress();
            _ = stream.PublishStateAsync(current.Url, position, paused, screenController.Engine.ScreenPosition,
                screenController.Engine.ScreenYaw, screenController.Engine.ScreenScale);
            video.SetOverlayTitle(current.Title, current.Source);
        }
    }

    // Runs every tick (cheap dictionary lookup) rather than once at startup because LocalContentId
    // is 0 until the player is actually logged into a character - a dev plugin can load at the
    // title screen, well before that's known.
    private void EnsureCharacterHasName()
    {
        var contentId = ReadLocalContentId();
        if (contentId == 0 || Cfg.CharacterDisplayNames.ContainsKey(contentId) || mainWindow.IsNamePromptActive)
        {
            return;
        }

        var suggested = ObjectTable.LocalPlayer?.Name.TextValue ?? "Player";
        mainWindow.RequestNamePrompt(suggested, name =>
        {
            Cfg.CharacterDisplayNames[contentId] = name;
            Cfg.Save();
            _ = stream.SendHelloAsync(name);
        });
    }

    // An admin cleared this player's name server-side (see AlphaChannel.Server's
    // /admin/reset-username) - drop the local record too so EnsureCharacterHasName re-prompts them
    // on the very next tick, same code path as the first-connect flow.
    private void OnRenameRequired()
    {
        var contentId = ReadLocalContentId();
        if (contentId != 0)
        {
            Cfg.CharacterDisplayNames.Remove(contentId);
            Cfg.Save();
        }
    }

    // A viewer's client receiving a host's stream.state - apply URL/position/pause and the
    // host's screen transform to this client's own local ScreenPainter, same "every client draws
    // its own copy" reasoning as VideoEngine.ApplyRemoteScreenTransform's own doc comment.
    private void OnRemoteState(AlphaChannel.Contracts.StreamControl message)
    {
        if (stream.Mode != StreamMode.Viewing || message.Url is not { Length: > 0 } url)
        {
            return;
        }

        video.Play(url);
        if (message.PositionSeconds is { } position)
        {
            video.Seek((float)position);
        }

        video.Pause(message.Paused ?? false);
        video.SetOverlayTitle(url, string.Empty);

        if (message.ScreenX is { } x && message.ScreenY is { } y && message.ScreenZ is { } z &&
            message.ScreenYaw is { } yaw && message.ScreenScale is { } scale)
        {
            screenController.Engine.ApplyRemoteScreenTransform(new Vector3(x, y, z), yaw, scale);
        }
    }

    private static unsafe ulong ReadLocalContentId()
    {
        var state = PlayerState.Instance();
        return state is null ? 0 : state->ContentId;
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler("/achannel");
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Framework.Update -= OnFrameworkUpdate;

        mainWindow.Dispose();
        stream.Dispose();
        screenController.Dispose();
        DxHandler.Dispose();
    }
}
