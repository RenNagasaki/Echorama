using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Echorama.DataClasses;
using Echorama.Helpers;
using Dalamud.Bindings.ImGui;


namespace Echorama.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin plugin;
    private Configuration configuration;
    internal static string activeTask = "";
    #region Logs
    private List<LogMessage> filteredLogsGeneral = [];
    private string filterLogsGeneralMethod = "";
    private string filterLogsGeneralMessage = "";
    private string filterLogsGeneralId = "";
    public static bool UpdateLogGeneralFilter = true;
    private bool resetLogGeneralFilter = true;
    #endregion

    private DateTime lastUpdate = DateTime.MinValue;
    // We give this window a hidden ID using ##
    // So that the user will see "My Amazing Window" as window title,
    // but for ImGui the ID is "My Amazing Window##With a hidden ID"
    public MainWindow(Plugin plugin, Configuration configuration)
        : base("My Amazing Window##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        this.configuration = configuration;
    }

    public void Dispose() { }

    public override unsafe void Draw()
    {
        using (var _ = ImRaii.Disabled(PanoramaHelper.DoingPanorama))
        {
            if (ImGui.CollapsingHeader("General Options:"))
            {
                var screenshotFolder = configuration.ScreenshotFolder;
                if (ImGui.InputText("Screenshot folder (Location where FFXIV Screenshots get saved)##ERScreenshotFolder", ref screenshotFolder, 256))
                {
                    configuration.ScreenshotFolder = screenshotFolder;
                    configuration.Save();
                }

                var panoramaFolder = configuration.PanoramaFolder;
                if (ImGui.InputText("Panorama folder(Working directory for this plugin)##ERPanoramaFolder", ref panoramaFolder, 256))
                {
                    configuration.PanoramaFolder = panoramaFolder;
                    configuration.Save();
                }
            }

            if (ImGui.CollapsingHeader("Panorama Options:"))
            {
                var screenshotScale = configuration.ScreenshotScale;
                if (ImGui.InputFloat("Screenshot Scale##ERScreenshotScale", ref screenshotScale, .5f))
                {
                    configuration.ScreenshotScale = screenshotScale;
                    configuration.Save();
                }

                var rowAmount = configuration.RowAmount;
                if (ImGui.InputInt("Amount of vertical rows (decides the vertical angle used, default 7)##ERRowAmount",
                                   ref rowAmount, 2))
                {
                    if (rowAmount % 2 == 0)
                        rowAmount--;

                    if (rowAmount < 1)
                        rowAmount = 1;

                    configuration.RowAmount = rowAmount;
                    configuration.Save();
                }

                var columnAmount = configuration.ColumnAmount;
                if (ImGui.InputInt(
                        "Amount of horizontal columns (decides the horizontal angle used, default 8)##ERColumnAmount",
                        ref columnAmount, 1))
                {
                    configuration.ColumnAmount = columnAmount;
                    configuration.Save();
                }
                var panoramaWidth = configuration.PanoramaWidth;
                if (ImGui.InputInt("Panorama Width (7000-16380)##ERPanoramaWidth", ref panoramaWidth, 2))
                {
                    if (panoramaWidth % 2 != 0)
                        panoramaWidth--;

                    if (panoramaWidth > 16380)
                        panoramaWidth = 16380;

                    if (panoramaWidth < 7000)
                        panoramaWidth = 7000;

                    configuration.PanoramaWidth = panoramaWidth;
                    configuration.PanoramaHeight = panoramaWidth / 2;
                    configuration.Save();
                }
                var panoramaHeight = configuration.PanoramaHeight;
                if (ImGui.InputInt("Panorama Height (3500-8190)##ERPanoramaHeight", ref panoramaHeight, 1))
                {
                    if (panoramaHeight > 8190)
                        panoramaHeight = 8190;

                    if (panoramaHeight < 3500)
                        panoramaHeight = 3500;

                    configuration.PanoramaHeight = panoramaHeight;
                    configuration.PanoramaWidth = panoramaHeight * 2;
                    configuration.Save();
                }
                var webPQuality = configuration.WebPQuality;
                if (ImGui.InputUInt("WebP Quality##ERWebPQuality", ref webPQuality, 1))
                {
                    if (webPQuality > 100)
                        webPQuality = 100;
                    
                    if (webPQuality < 1)
                        webPQuality = 1;

                    configuration.WebPQuality = webPQuality;
                    configuration.Save();
                }
                
                /*var showCharacter = configuration.ShowCharacter;
                if (ImGui.Checkbox("Show character in panorama (moves camera infront of player)##ERShowCharacter",
                                   ref showCharacter))
                {
                    configuration.ShowCharacter = showCharacter;
                    configuration.Save();
                }*/
                
                var multicoreGen = configuration.MulticoreGen;
                if (ImGui.Checkbox("Use Multicore Generation (harder on the cpu, but faster)##ERMulticoreGen",
                                   ref multicoreGen))
                {
                    configuration.MulticoreGen = multicoreGen;
                    configuration.Save();
                }
                
                ImGui.NewLine();
                var keepImages = configuration.KeepImages;
                if (ImGui.Checkbox("Keep Screenshots after generation##ERKeepImages",
                                   ref keepImages))
                {
                    configuration.KeepImages = keepImages;
                    configuration.Save();
                }
                ImGui.SameLine();
                var keepTemp = configuration.KeepTemp;
                if (ImGui.Checkbox("Keep Temp folder after generation##ERKeepTemp",
                                   ref keepTemp))
                {
                    configuration.KeepTemp = keepTemp;
                    configuration.Save();
                }
                ImGui.SameLine();
                var keepStitches = configuration.KeepStitches;
                if (ImGui.Checkbox("Keep Stitches folder after generation##ERKeepStitches",
                                   ref keepStitches))
                {
                    configuration.KeepStitches = keepStitches;
                    configuration.Save();
                }
            }
            
            var panoramaName = configuration.PanoramaName;
            if (ImGui.InputText("Panorama Name (Will restart with already taken screenshots, leave empty to create new ones)##ERPanoramaName",
                                ref panoramaName))
            {
                configuration.PanoramaName = panoramaName;
                configuration.Save();
            }

            if (ImGui.Button("Create Panorama##ERCreatePano"))
            {
                Plugin.PanoramaHelper.DoPanorama();
            }

            ImGui.Text($"Currently working on:");
            ImGui.PushStyleColor(ImGuiCol.Text, Constants.ACTIVETASKCOLOR);
            ImGui.Text($"{activeTask}");
            ImGui.PopStyleColor();
        }

        if (ImGui.CollapsingHeader("Log Options:"))
        {
            var showDebugLog = this.configuration.ShowGeneralDebugLog;
            if (ImGui.Checkbox("Show debug logs", ref showDebugLog))
            {
                this.configuration.ShowGeneralDebugLog = showDebugLog;
                this.configuration.Save();
                UpdateLogGeneralFilter = true;
            }
            var showErrorLog = this.configuration.ShowGeneralErrorLog;
            if (ImGui.Checkbox("Show error logs", ref showErrorLog))
            {
                this.configuration.ShowGeneralErrorLog = showErrorLog;
                this.configuration.Save();
                UpdateLogGeneralFilter = true;
            }
            var jumpToBottom = this.configuration.GeneralJumpToBottom;
            if (ImGui.Checkbox("Always jump to bottom", ref jumpToBottom))
            {
                this.configuration.GeneralJumpToBottom = jumpToBottom;
                this.configuration.Save();
            }
        }
        DrawLogTable("General", configuration.GeneralJumpToBottom, ref filteredLogsGeneral, ref UpdateLogGeneralFilter, ref resetLogGeneralFilter, ref filterLogsGeneralMethod, ref filterLogsGeneralMessage, ref filterLogsGeneralId);
    }

    private void DrawLogTable(string logType, bool scrollToBottom, ref List<LogMessage> filteredLogs, ref bool updateLogs, ref bool resetLogs, ref string filterMethod, ref string filterMessage, ref string filterId)
    {
        var newData = false;
        if (ImGui.CollapsingHeader("Log:"))
        {
            if (filteredLogs == null)
            {
                updateLogs = true;
            }

            if (updateLogs || (resetLogs && (filterMethod.Length == 0 || filterMessage.Length == 0 || filterId.Length == 0)))
            {
                filteredLogs = LogHelper.RecreateLogList();
                updateLogs = true;
                resetLogs = false;
                newData = true;
            }
            if (ImGui.BeginTable($"Log Table##{logType}LogTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupScrollFreeze(0, 2); // Make top row always visible
                ImGui.TableSetupColumn("Timestamp", ImGuiTableColumnFlags.WidthFixed, 75f);
                ImGui.TableSetupColumn("Method", ImGuiTableColumnFlags.WidthFixed, 150f);
                ImGui.TableSetupColumn("Message", ImGuiTableColumnFlags.None, 500f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableHeadersRow();
                ImGui.TableNextColumn();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilter{logType}LogMethod", ref filterMethod, 40) || (filterMethod.Length > 0 && updateLogs))
                {
                    var method = filterMethod;
                    filteredLogs = filteredLogs.FindAll(p => p.method.ToLower().Contains(method.ToLower()));
                    updateLogs = true;
                    resetLogs = true;
                    newData = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilter{logType}LogMessage", ref filterMessage, 80) || (filterMessage.Length > 0 && updateLogs))
                {
                    var message = filterMessage;
                    filteredLogs = filteredLogs.FindAll(p => p.message.ToLower().Contains(message.ToLower()));
                    updateLogs = true;
                    resetLogs = true;
                    newData = true;
                }
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.InputText($"##EKFilter{logType}LogId", ref filterId, 40) || (filterId.Length > 0 && updateLogs))
                {
                    var id = filterId;
                    filteredLogs = filteredLogs.FindAll(p => p.eventId.Id.ToString().ToLower().Contains(id.ToLower()));
                    updateLogs = true;
                    resetLogs = true;
                    newData = true;
                }
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty || updateLogs)
                {
                    switch (sortSpecs.Specs.ColumnIndex)
                    {
                        case 0:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => DateTime.Compare(a.timeStamp, b.timeStamp));
                            else
                                filteredLogs.Sort((a, b) => DateTime.Compare(b.timeStamp, a.timeStamp));
                            break;
                        case 1:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => string.Compare(a.method, b.method));
                            else
                                filteredLogs.Sort((a, b) => string.Compare(b.method, a.method));
                            break;
                        case 2:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => string.Compare(a.message, b.message));
                            else
                                filteredLogs.Sort((a, b) => string.Compare(b.message, a.message));
                            break;
                        case 3:
                            if (sortSpecs.Specs.SortDirection == ImGuiSortDirection.Ascending)
                                filteredLogs.Sort((a, b) => string.Compare(a.eventId.Id.ToString(), b.eventId.Id.ToString()));
                            else
                                filteredLogs.Sort((a, b) => string.Compare(b.eventId.Id.ToString(), a.eventId.Id.ToString()));
                            break;
                    }

                    updateLogs = false;
                    sortSpecs.SpecsDirty = false;
                }
                foreach (var logMessage in filteredLogs)
                {
                    ImGui.TableNextRow();
                    ImGui.PushStyleColor(ImGuiCol.Text, logMessage.color);
                    ImGui.PushTextWrapPos();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.timeStamp.ToString("HH:mm:ss.fff"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.method);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.message);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(logMessage.eventId.Id.ToString());
                    ImGui.PopStyleColor();
                }

                if (scrollToBottom && newData)
                {
                    ImGui.SetScrollHereY();
                }

                ImGui.EndTable();
            }
        }
    }

    internal static void ActiveTask(string methodName, string curActiveTask, EREventId eventId)
    {
        activeTask = curActiveTask;
        LogHelper.Info(methodName, activeTask, eventId);
    }
}
