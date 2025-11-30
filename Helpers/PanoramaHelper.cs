using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using Echorama.DataClasses;
using Echorama.Enums;
using Echorama.Windows;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using ImageMagick;
using Lumina.Excel.Sheets.Experimental;
using _configuration = Echorama.DataClasses.Configuration;
using Timer = System.Timers.Timer;
#pragma warning disable PendingExcelSchema

namespace Echorama.Helpers;

public unsafe class PanoramaHelper: IDisposable
{
    internal static bool DoingPanorama = false;
    public static TerritoryInfo* AreaInfo => TerritoryInfo.Instance();
    public static List<EREventId> QueueItems = new List<EREventId>();
    public static Thread QueueThread = new Thread(WorkQueue);
    public static bool StopThread = false;
    private static EREventId ScreenshotEventId;

    private const int ScreenshotKey = 551;
    private delegate byte IsInputIdClickedDelegate(UIInputData* uiInputData, int key);
    [Signature("E9 ?? ?? ?? ?? 83 7F ?? ?? 0F 8F ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8B CB", DetourName = nameof(IsInputIdClickedDetour))]
    private readonly Hook<IsInputIdClickedDelegate>? IsInputIdClickedHook = null;

    private delegate nint ScreenShotCallbackDelegate(nint a1, int a2);
    [Signature("48 89 5C 24 08 57 48 83 EC 20 BB 8B 07 00 00", DetourName = nameof(ScreenShotCallbackDetour))]
    private readonly Hook<ScreenShotCallbackDelegate>? ScreenShotCallbackHook = null;

    private static _configuration _configuration;
    private static string ptoGenPath;
    private static string ptoVarPath;
    private static string cpFindPath;
    private static string cpCleanPath;
    private static string linefindPath;
    private static string panoModifyPath;
    private static string autoOptimiserPath;
    private static string nonaPath;
    private static string enblendPath;
    private static string verdandiPath;

    private bool takeScreenshotPressed;
    private uint oldWidth;
    private uint oldHeight;
    private readonly float _origMaxVRota;
    private readonly float _origMinVRota;
    private static float origCamX;

    public PanoramaHelper(Configuration configuration)
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);
        _configuration = configuration;

        var camera = Common.CameraManager->worldCamera;
        _origMaxVRota = camera->maxVRotation;
        _origMinVRota = camera->minVRotation;
        _configuration.MaxVRota = _origMaxVRota;
        _configuration.MinVRota = _origMinVRota;

        SetupHuginPaths();
        QueueThread.Start();
    }

    private void SetupHuginPaths()
    {
        if (Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows)
        {
            ptoGenPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "pto_gen.exe");
            ptoVarPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "pto_var.exe");
            cpFindPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "cpfind.exe");
            cpCleanPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "cpclean.exe");
            linefindPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "linefind.exe");
            panoModifyPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "pano_modify.exe");
            autoOptimiserPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "autooptimiser.exe");
            nonaPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "nona.exe");
            enblendPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "enblend.exe");
            verdandiPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "verdandi.exe");
        }
        else
        {
            ptoGenPath = "pto_gen";
            ptoVarPath = "pto_var";
            cpFindPath = "cpfind";
            cpCleanPath = "cpclean";
            panoModifyPath = "pano_modify";
            autoOptimiserPath = "autooptimiser";
            nonaPath = "nona";
            enblendPath = "enblend";
            verdandiPath = "verdandi";
        }
    }

    static void WorkQueue()
    {
        try
        {
            while (!StopThread)
            {
                var runningItems = 0;
                foreach (EREventId queueItem in QueueItems)
                {
                    if (queueItem.Status == QueueStatus.Running)
                        runningItems++;
                    else if (runningItems < _configuration.ParallelThreads && queueItem.Status == QueueStatus.Created)
                    {
                        StartPanoramaProcess(queueItem);
                        queueItem.Status = QueueStatus.Running;
                        runningItems++;
                    }

                    if (runningItems >= _configuration.ParallelThreads)
                        break;
                }

                Thread.Sleep(100);
            }
        }
        catch { }
    }

    private void MoveCameraInFrontOfPlayer()
    {
        try
        {
            /*var cameraManager = CameraManager.Instance();
            var sceneCamera = cameraManager->GetActiveCamera()->SceneCamera;
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name,
                            $"OldPos -> {sceneCamera.Position.X}:{sceneCamera.Position.Y}:{sceneCamera.Position.Z}",
                            new EREventId());

            var vector = sceneCamera.LookAtVector - sceneCamera.Position;
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Vector -> {vector.X}:{vector.Y}:{vector.Z}",
                            new EREventId());

            sceneCamera.Position += vector;
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name,
                            $"NewPos -> {sceneCamera.Position.X}:{sceneCamera.Position.Y}:{sceneCamera.Position.Z}",
                            new EREventId());

            var camera = Common.CameraManager->worldCamera;
            camera->lockPosition = 0;
            //camera->mode = 1;
            camera->viewX += 100f;
            camera->x += 100f;*/
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, new EREventId());

        }
    }

    internal void DoPanorama()
    {
        var currentEventId = new EREventId();
        try
        {
            if (!DoingPanorama)
            {
                ScreenshotEventId = currentEventId;
                var camera = Common.CameraManager->worldCamera;
                LogHelper.Start(MethodBase.GetCurrentMethod()!.Name, currentEventId);
                LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, "Creating panorama", currentEventId);
                ScreenshotEventId.Anchor = _configuration.RowAmount > 1
                                              ? ((_configuration.RowAmount - 1) / 2) * _configuration.ColumnAmount
                                              : 0;
                //anchor -= (_configuration.ColumnAmount - 1);
                var logMsg = $"VAngle: {ScreenshotEventId.VerticalAngle}, HAngle: {ScreenshotEventId.HorizontalAngle}, Anchor: {ScreenshotEventId.Anchor}, CurrentFOV: {camera->currentFoV}";
                MainWindow.ActiveTask(
                    MethodBase.GetCurrentMethod().Name,
                    $"Creating panorama screenshots, hands of mouse and keyboard(or controller)!!!\r\n{logMsg}",
                    currentEventId
                );

                var localPlayer = Plugin.ClientState.LocalPlayer;

                var weatherId = WeatherManager.Instance()->GetCurrentWeather();
                var weatherSheet = Plugin.DataManager.GetExcelSheet<Weather>(Plugin.ClientLanguage);
                var weatherName = weatherSheet.GetRow(weatherId).Name.ExtractText();
                var eorzeaTime = EorzeanDateTime(Framework.Instance()->ClientTime.EorzeaTime);
                var map = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Map>(Plugin.ClientLanguage)!.GetRow(Plugin.ClientState.MapId);
                var territoryType = Plugin.DataManager.GetExcelSheet<TerritoryTypeTransient>(Plugin.ClientLanguage)!.GetRow(Plugin.ClientState.TerritoryType);
                ScreenshotEventId.PanoramaLocation = MapUtil.WorldToMap(localPlayer.Position, map.OffsetX, map.OffsetY, territoryType.OffsetZ, map.SizeFactor, true);
                var locationString =
                    $"{ScreenshotEventId.PanoramaLocation.X.ToString("F0", CultureInfo.InvariantCulture)}_" +
                    $"{ScreenshotEventId.PanoramaLocation.Y.ToString("F0", CultureInfo.InvariantCulture)}_" +
                    $"{ScreenshotEventId.PanoramaLocation.Z.ToString("F0", CultureInfo.InvariantCulture)}";
                currentEventId.PanoramaPath =
                    Path.Join(_configuration.PanoramaFolder, $"{GetTerritoryName()}_{GetTerritorySubName()}_{GetAreaName()}_{GetSubAreaName()}_{weatherName}_{locationString}_{eorzeaTime}");
                locationString =
                    $"{ScreenshotEventId.PanoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_" +
                    $"{ScreenshotEventId.PanoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_" +
                    $"{ScreenshotEventId.PanoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}";
                currentEventId.PanoramaName =
                    $"{GetTerritoryName()}_{GetTerritorySubName()}_{GetAreaName()}_{GetSubAreaName()}_{weatherName}_{locationString}_{eorzeaTime}"
                ;

                if (_configuration.ShowCharacter)
                {
                    MoveCameraInFrontOfPlayer();
                }

                ScreenshotEventId.VerticalAngle =
                    180f / (_configuration.RowAmount > 1 ? _configuration.RowAmount - 1 : _configuration.RowAmount);
                ScreenshotEventId.HorizontalAngle = 360f / _configuration.ColumnAmount;

                ScreenshotEventId.ImageCountH = 0;
                ScreenshotEventId.ImageCountV = 1;

                camera->mode = 0;

                var device = Device.Instance();
                oldWidth = device->Width;
                oldHeight = device->Height;
                float curWidth = oldWidth;
                float curHeight = oldHeight;
                if (_configuration.ScreenshotScale > 1)
                {
                    device->NewWidth = oldWidth * (uint)_configuration.ScreenshotScale;
                    device->NewHeight = oldHeight * (uint)_configuration.ScreenshotScale;

                    curWidth = device->NewWidth;
                    curHeight = device->NewHeight;
                    device->RequestResolutionChange = 1;
                }
                Timer timer = new Timer(5000);
                timer.Elapsed += (_, __) =>
                {
                    RaptureAtkModule.Instance()->SetUiVisibility(false);
                    DoingPanorama = true;
                    ScreenshotEventId.Fov = 2 * Math.Atan(Math.Tan(camera->currentFoV / 2.0) * (curWidth / curHeight)) *
                                           180.0 / Math.PI;
                    CalculatePanoramaLogic(currentEventId);
                    timer.Stop();
                };
                timer.Start();
            }
            else
            {
                LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, new Exception("Already in the process of taking panorama screenshots. Wait until all screenshots are taken!"), currentEventId);
            }
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    public string GetSubAreaName()
    {
        var row = TerritoryInfo.Instance()->SubAreaPlaceNameId;
        var subArea = Plugin.DataManager.GetExcelSheet<PlaceName>(Plugin.ClientLanguage).GetRow(row)
                            .Name.ExtractText();
        subArea = string.IsNullOrWhiteSpace(subArea) ? "NoSubArea" : subArea;
        return subArea;
    }

    public string GetAreaName()
    {
        var row = TerritoryInfo.Instance()->AreaPlaceNameId;
        var subArea = Plugin.DataManager.GetExcelSheet<PlaceName>(Plugin.ClientLanguage).GetRow(row)
                            .Name.ExtractText();
        subArea = string.IsNullOrWhiteSpace(subArea) ? "NoArea" : subArea;
        return subArea;
    }

    public string GetTerritorySubName()
    {
        var map = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Map>(Plugin.ClientLanguage).GetRow(Plugin.ClientState.MapId);
        var placeNameSub = Plugin.DataManager.GetExcelSheet<PlaceName>(Plugin.ClientLanguage).GetRow(map.PlaceNameSub.RowId);
        var territoryName = placeNameSub.Name.ExtractText() ?? "NoTerritorySub";

        return territoryName;
    }

    public string GetTerritoryName()
    {
        var map = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Experimental.Map>(Plugin.ClientLanguage).GetRow(Plugin.ClientState.MapId);
        var placeName = Plugin.DataManager.GetExcelSheet<PlaceName>(Plugin.ClientLanguage).GetRow(map.PlaceName.RowId);
        var territoryName = placeName.Name.ExtractText() ?? "NoTerritory";

        return territoryName;
    }

    private string EorzeanDateTime(long eorzeanUnixTime)
    {
        var seconds = eorzeanUnixTime;
        var minutes = seconds / 60;
        var bells = minutes / 60;
        var suns = bells / 24;
        var weeks = suns / 8;
        var moons = weeks / 4;
        var years = moons / 12;
        return $"{(bells % 24).ToString().PadLeft(2,'0')}{(minutes % 60).ToString().PadLeft(2,'0')}";
    }

    public void CalculatePanoramaLogic(EREventId eventId)
    {
        try
        {
            eventId.ImageCountH++;
            if (eventId.ImageCountH > _configuration.ColumnAmount)
            {
                eventId.ImageCountH = 1;
                eventId.ImageCountV++;
                if (eventId.ImageCountV > _configuration.RowAmount)
                {
                    DoingPanorama = false;
                    var camera = Common.CameraManager->worldCamera;
                    camera->mode = 1;
                    camera->maxVRotation = this._origMaxVRota;
                    camera->minVRotation = this._origMinVRota;
                    RaptureAtkModule.Instance()->SetUiVisibility(true);
                    if (_configuration.ScreenshotScale > 1)
                    {
                        var device = Device.Instance();
                        device->NewWidth = oldWidth;
                        device->NewHeight = oldHeight;
                        device->RequestResolutionChange = 1;
                    }

                    QueueItems.Add(eventId);
                    return;
                }
            }

            DoCameraMovement(eventId);
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

    private void DoCameraMovement(EREventId eventId)
    {
        try
        {
            var camera = Common.CameraManager->worldCamera;
            camera->maxVRotation = Constants.MAXVROTA;
            camera->minVRotation = -Constants.MAXVROTA;
            var hRota = MathF.PI - (MathF.PI / (_configuration.ColumnAmount / 2f) * (eventId.ImageCountH - 1));
            camera->currentHRotation = hRota;
            var vRotaStep = Constants.MAXVROTA / ((_configuration.RowAmount - 1f) / 2);
            var vRota = _configuration.RowAmount > 1 ? Constants.MAXVROTA - (vRotaStep * (eventId.ImageCountV - 1)) : 0;
            camera->currentVRotation = vRota;

            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Set camera to new angles. VRotation: {vRota} HRotation: {hRota}", eventId);
            takeScreenshotPressed = true;
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

    private static void FinishingTouches(EREventId eventId, string panoramaFolder, string tempFolder,string stitchesFolder,string imagesFolder)
    {
        try
        {
            if (!_configuration.KeepStitches && !_configuration.KeepTemp && !_configuration.KeepImages)
                Directory.Delete(panoramaFolder, true);
            else
            {
                if (!_configuration.KeepImages)
                    Directory.Delete(imagesFolder, true);

                if (!_configuration.KeepTemp)
                    Directory.Delete(tempFolder, true);

                if (!_configuration.KeepStitches)
                    Directory.Delete(stitchesFolder, true);
            }
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

    private static void StartPanoramaProcess(EREventId eventId)
    {
        try
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var threadEventId = eventId;
                    var newPanoramaFolder = threadEventId.PanoramaPath;
                    var newPanoramaName = threadEventId.PanoramaName;
                    var tempPanoramaFolder = Path.Join(newPanoramaFolder, "temp");
                    var stitchesPanoramaFolder = Path.Join(newPanoramaFolder, "stitches");
                    var finishedPanoramaFolder = Path.Join(_configuration.PanoramaFolder, "finished_panorama");
                    var finishedPanoramaWebpFolder = Path.Join(_configuration.PanoramaFolder, "finished_panorama_webp");
                    LogHelper.Debug("StartPanoramaProcess", "Creating necessary folder structure", threadEventId);
                    if (!Directory.Exists($@"{newPanoramaFolder}"))
                        Directory.CreateDirectory($@"{newPanoramaFolder}");
                    if (!Directory.Exists(tempPanoramaFolder))
                        Directory.CreateDirectory(tempPanoramaFolder);
                    if (!Directory.Exists(stitchesPanoramaFolder))
                        Directory.CreateDirectory(stitchesPanoramaFolder);
                    if (!Directory.Exists(finishedPanoramaFolder))
                        Directory.CreateDirectory(finishedPanoramaFolder);
                    if (!Directory.Exists(finishedPanoramaWebpFolder))
                        Directory.CreateDirectory(finishedPanoramaWebpFolder);

                    MainWindow.ActiveTask("StartPanoramaProcess | pto_gen","Creating panorama of screenshots!\r\nCalling pto_gen", threadEventId);
                    CallCMD(
                        threadEventId,
                        ptoGenPath,
                        $"-f {eventId.Fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{Path.Join(tempPanoramaFolder, $"panorama-1.pto")}\" \"{Path.Join(newPanoramaFolder, "images", "row*.jpg")}\"",
                        "pto_gen"
                    );

                    if (StopThread)
                        return;

                    MainWindow.ActiveTask("StartPanoramaProcess | pto_var","Creating panorama of screenshots!\r\nCalling pto_var", threadEventId);
                    int imageCount = 0; 
                    float hAngle = 0;
                    float vAngle = 90;
                    
                    for (int y = 0; y < _configuration.RowAmount; y++)
                    {
                        for (int x = 0; x < _configuration.ColumnAmount; x++)
                        {
                            var tempHAngle = hAngle + (x * eventId.HorizontalAngle);
                            var tempVAngle = vAngle - (y * eventId.VerticalAngle);
                            CallCMD(
                                threadEventId,
                                ptoVarPath,
                                $"--set=y{imageCount}={tempHAngle.ToString("F6", CultureInfo.InvariantCulture)},p{imageCount}={tempVAngle.ToString("F6", CultureInfo.InvariantCulture)},v{imageCount}={eventId.Fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{Path.Join(tempPanoramaFolder, $"panorama{imageCount}.pto")}\" \"{Path.Join(tempPanoramaFolder, $"panorama{imageCount - 1}.pto")}\"",
                                "pto_var"
                            );
                            imageCount++;
                        }
                    }

                    CallCMD(
                        threadEventId,
                        ptoVarPath,
                        $"--modify-opt --opt=y,p,r,!v --anchor={eventId.Anchor} --color-anchor={eventId.Anchor} -o \"{Path.Join(tempPanoramaFolder, $"anchored.pto")}\" \"{Path.Join(tempPanoramaFolder, $"panorama{imageCount - 1}.pto")}\"",
                        "pto_var"
                    );

                    if (StopThread)
                        return;

                    MainWindow.ActiveTask("StartPanoramaProcess | cpfind","Creating panorama of screenshots!\r\nCalling cpfind", threadEventId);
                    CallCMD(
                        threadEventId,
                        cpFindPath,
                        $"--prealigned -o \"{Path.Join(tempPanoramaFolder, $"cpfind.pto")}\" \"{Path.Join(tempPanoramaFolder, "anchored.pto")}\"",
                        "cpfind"
                    );

                    if (StopThread)
                        return;

                    MainWindow.ActiveTask("StartPanoramaProcess | cpclean","Creating panorama of screenshots!\r\nCalling cpclean", threadEventId);
                    CallCMD(
                        threadEventId,
                        cpCleanPath,
                        $"-o \"{Path.Join(tempPanoramaFolder, $"cpclean.pto")}\" \"{Path.Join(tempPanoramaFolder, $"cpfind.pto")}\"",
                        "cpclean"
                    );

                    if (StopThread)
                        return;

                    /*MainWindow.ActiveTask("StartPanoramaProcess | linefind","Creating panorama of screenshots!\r\nCalling Linefind", threadEventId);
                    CallCMD(
                        threadEventId,
                        linefindPath,
                        $"-o \"{Path.Join(tempPanoramaFolder, $"linefind.pto")}\" \"{Path.Join(tempPanoramaFolder, $"cpclean.pto")}\"",
                        "linefind"
                    );*/

                    MainWindow.ActiveTask("StartPanoramaProcess | autooptimiser","Creating panorama of screenshots!\r\nCalling autooptimiser", threadEventId);
                    CallCMD(
                        threadEventId,
                        autoOptimiserPath,
                        $"-m -o \"{Path.Join(tempPanoramaFolder, $"autooptimiser.pto")}\" \"{Path.Join(tempPanoramaFolder, $"cpclean.pto")}\"",
                        "autooptimiser"
                    );

                    if (StopThread)
                        return;

                    /*var lines = File.ReadAllLines(Path.Join(tempPanoramaFolder, $"{nextPto}.pto"));
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("p "))
                        {
                            lines[i] = ReplaceDimensionsInPLine(lines[i], _configuration.PanoramaWidth, _configuration.PanoramaHeight);
                            break;
                        }
                    }
                    File.WriteAllLines(Path.Join(tempPanoramaFolder, "resized.pto"), lines);*/

                    MainWindow.ActiveTask("StartPanoramaProcess | pano_modify","Creating panorama of screenshots!\r\nCalling pano_modify", threadEventId);
                    CallCMD(
                        threadEventId,
                        panoModifyPath,
                        $"--crop=AUTO --canvas=AUTO -o \"{Path.Join(tempPanoramaFolder, "shifted.pto")}\" \"{Path.Join(tempPanoramaFolder, "autooptimiser.pto")}\"",
                        "pano_modify"
                    );

                    if (StopThread)
                        return;
                    
                    var pto = File.ReadAllLines(Path.Join(tempPanoramaFolder, "shifted.pto"));
                    int w=0,h=0;
                    foreach (var line in pto)
                    {
                        if (line.StartsWith("p "))
                        {
                            var parts = line.Split(' ');
                            foreach (var t in parts)
                            {
                                if (t.StartsWith("w")) w = int.Parse(t.Substring(1));
                                if (t.StartsWith("h")) h = int.Parse(t.Substring(1));
                            }
                            break;
                        }
                    }
                    double scale = Math.Min(1.0, Math.Min(_configuration.PanoramaWidth / (double)w, _configuration.PanoramaHeight / (double)h));
                    int percent = (int)Math.Floor(scale * 100.0);

                    var nextPto = "shifted";
                    if (percent < 100)
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | pano_modify",
                                              "Creating panorama of screenshots!\r\nCalling pano_modify",
                                              threadEventId);
                        CallCMD(
                            threadEventId,
                            panoModifyPath,
                            $"--canvas={percent}% --crop=AUTO -o \"{Path.Join(tempPanoramaFolder, "auto_sized.pto")}\" \"{Path.Join(tempPanoramaFolder, "shifted.pto")}\"",
                            "pano_modify"
                        );
                        nextPto = "auto_sized";
                    }

                    if (StopThread)
                        return;

                    MainWindow.ActiveTask("StartPanoramaProcess | nona","Creating panorama of screenshots!\r\nCalling nona", threadEventId);
                    CallCMD(
                        threadEventId,
                        nonaPath,
                        $"-o \"{Path.Join(stitchesPanoramaFolder, "stitch")}\" \"{Path.Join(tempPanoramaFolder, $"{nextPto}.pto")}\"",
                        "nona"
                    );

                    if (StopThread)
                        return;

                    if (!_configuration.MulticoreGen)
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | enblend","Creating panorama of screenshots!\r\nCalling enblend <--- LAST STEP! Takes some time!", threadEventId);
                        CallCMD(
                            threadEventId,
                            enblendPath,
                            $"-o \"{Path.Join(finishedPanoramaFolder, $"{newPanoramaName}.tif")}\" \"{Path.Join(stitchesPanoramaFolder, "stitch*.tif")}\"",
                            "enblend"
                        );
                    }
                    else
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | verdandi","Creating panorama of screenshots!\r\nCalling verdandi - Takes some time!", threadEventId);
                        CallCMD(
                            threadEventId,
                            verdandiPath,
                            $"-o \"{Path.Join(finishedPanoramaFolder, $"{newPanoramaName}.tif")}\" \"{Path.Join(stitchesPanoramaFolder, "stitch*.tif")}\"",
                            "verdandi"
                        );
                    }

                    if (StopThread)
                        return;

                    MainWindow.ActiveTask("StartPanoramaProcess | convert","Creating panorama of screenshots!\r\nConverting to webp", threadEventId);
                    ConvertTifToWebp(
                        threadEventId,
                        Path.Join(finishedPanoramaFolder, $"{newPanoramaName}.tif"),
                        Path.Join(finishedPanoramaWebpFolder, $"{newPanoramaName}.webp")
                    );

                    FinishingTouches(eventId, newPanoramaFolder, tempPanoramaFolder, stitchesPanoramaFolder, Path.Join(newPanoramaFolder, "images"));
                    MainWindow.ActiveTask("StartPanoramaProcess","DONE!!!", threadEventId);
                    LogHelper.End("StartPanoramaProcess", threadEventId);
                }
                catch (Exception e)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
                }
            });
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }
    
    internal static void ConvertTifToWebp(EREventId eventId ,string tifPath, string webpPath)
    {
        try {
            using (var image = new MagickImage(tifPath))
            {
                image.Format = MagickFormat.WebP;
                image.Depth = 32;
                image.Quality = _configuration.WebPQuality; // verlustbehaftet
                image.Write(webpPath);
            }

        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

    private static void CallCMD(EREventId eventId ,string exePath, string command, string methodExtra)
    {
        try
        {
            var process = new Process();

            if (Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows)
            {
                process.StartInfo.FileName = "cmd.exe"; // oder "bash" unter Linux/macOS
                process.StartInfo.Arguments = @$"/c {exePath} {command}";
            }
            else
            {
                process.StartInfo.FileName = "/bin/bash"; // oder "bash" unter Linux/macOS
                process.StartInfo.Arguments = @$"-c {exePath} {command}";
            }

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name + $" | {methodExtra}", @$"Calling command: '{exePath} {command}'", eventId);
            process.Start();

            while (!process.HasExited)
            {
                string output = process.StandardOutput.ReadLine();
                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name + $" | {methodExtra}", output, eventId);
            }
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

    private nint ScreenShotCallbackDetour(nint a1, int a2)
    {
        nint outcome = ScreenShotCallbackHook!.Original(a1, a2);

        if (DoingPanorama)
        {
            var imageCountVert = ScreenshotEventId.ImageCountV;
            var imageCountHor = ScreenshotEventId.ImageCountH;
            System.Threading.Tasks.Task.Run(() =>
            {
                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Screenshot taken. Row: {imageCountVert.ToString().PadLeft(2, '0')} Column: {imageCountHor.ToString().PadLeft(2, '0')}", ScreenshotEventId);

                MoveLastScreenshot(imageCountVert, imageCountHor);
            });
            CalculatePanoramaLogic(ScreenshotEventId);
        }

        return outcome;
    }

    internal void Init()
    {
        IsInputIdClickedHook?.Enable();
        ScreenShotCallbackHook?.Enable();
    }

    public void Dispose()
    {
        IsInputIdClickedHook?.Dispose();
        ScreenShotCallbackHook?.Dispose();
        try
        {
            StopThread = true;
            QueueThread.Interrupt();
        }
        catch { }
    }

    private byte IsInputIdClickedDetour(UIInputData* uiInputData, int key)
    {
        try
        {
            byte outcome = IsInputIdClickedHook!.Original(uiInputData, key);
            if (key == ScreenshotKey && takeScreenshotPressed && DoingPanorama)
            {
                takeScreenshotPressed = false;
                outcome = 1;
            }

            return outcome;
        }
        catch(Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, ScreenshotEventId);
        }

        return 0;
    }

    private void MoveLastScreenshot(int imageCountVert, int imageCountHor)
    {
        try
        {
            var directory = new DirectoryInfo(_configuration.ScreenshotFolder);

            FileInfo newScreenshot = directory
                                     .GetFiles()
                                     .OrderByDescending(f => f.LastWriteTime)
                                     .FirstOrDefault();

            var imagesPath = Path.Join(ScreenshotEventId.PanoramaPath, "images");
            if (!Directory.Exists($@"{imagesPath}"))
                Directory.CreateDirectory($@"{imagesPath}");

            var screenshotName =
                $"row{imageCountVert.ToString().PadLeft(2, '0')}_col{imageCountHor.ToString().PadLeft(2, '0')}.jpg";
            var moved = false;
            var moveTries = 0;

            while (!moved && moveTries < 5)
            {
                try
                {
                    newScreenshot.MoveTo(Path.Join(imagesPath, screenshotName), true);
                    moved = true;
                }
                catch { }
                Thread.Sleep(100);
                moveTries++;
            }

            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Screenshot moved. Row: {imageCountVert.ToString().PadLeft(2, '0')} Column: {imageCountHor.ToString().PadLeft(2, '0')}", ScreenshotEventId);
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, ScreenshotEventId);
        }
    }
}
