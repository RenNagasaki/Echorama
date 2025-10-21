using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Echorama.DataClasses;
using Echorama.Helpers;
using Echorama.Windows;

namespace Echorama;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    internal static PanoramaHelper PanoramaHelper { get; private set; } = null!;

    private const string CommandName = "/er";

    internal static ClientLanguage ClientLanguage => ClientLanguage.English;//ClientState.ClientLanguage;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Echorama");
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Hypostasis.Hypostasis.Initialize(this, PluginInterface);
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        LogHelper.Setup(Configuration);
        PanoramaHelper = new PanoramaHelper(Configuration);
        PanoramaHelper.Init();
        MainWindow = new MainWindow(this, Configuration);

        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Creates a 360 panorama picture of your current location. /er"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }



    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        PanoramaHelper.Dispose();

        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        Hypostasis.Hypostasis.Dispose(false);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainUI() => MainWindow.Toggle();
}
