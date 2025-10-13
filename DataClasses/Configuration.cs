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
    public string PanoramaName { get; set; } = "";
    public float ScreenshotScale { get; set; } = 1;
    public int ColumnAmount { get; set; } = 8;
    public int RowAmount { get; set; } = 7;
    public uint WebPQuality { get; set; } = 100;
    public int PanoramaWidth { get; set; } = 16380;
    public int PanoramaHeight { get; set; } = 8190;
    public bool ShowGeneralDebugLog { get; set; } = true;
    public bool ShowGeneralErrorLog { get; set; } = true;
    public bool GeneralJumpToBottom { get; set; } = true;
    public bool ShowCharacter { get; set; } = false;
    public bool MulticoreGen { get; set; } = false;
    
    public bool KeepImages { get; set; } = false;
    public bool KeepTemp { get; set; } = false;
    public bool KeepStitches { get; set; } = false;

    public float MaxVRota { get; set; } = 0;
    public float MinVRota { get; set; } = 0;

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
