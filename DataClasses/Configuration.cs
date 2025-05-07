using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace Echorama.DataClasses;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string PanoramaFolder { get; set; } = "";
    public string ScreenshotFolder { get; set; } = "";
    public float ScreenshotScale { get; set; } = 1;
    public int ColumnAmount { get; set; } = 8;
    public int RowAmount { get; set; } = 7;
    public int PanoramaWidth { get; set; } = 16380;
    public int PanoramaHeight { get; set; } = 8190;
    public bool ShowGeneralDebugLog { get; set; } = true;
    public bool ShowGeneralErrorLog { get; set; } = true;
    public bool GeneralJumpToBottom { get; set; } = true;
    public bool ExperimentalCPDetection { get; set; } = false;
    public bool MulticoreGen { get; set; } = false;

    public float MaxVRota { get; set; } = 0;
    public float MinVRota { get; set; } = 0;

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
