﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using Echorama.DataClasses;
using Echorama.Windows;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Math;
using ImageMagick;
using Lumina.Excel.Sheets.Experimental;
using Configuration = Echorama.DataClasses.Configuration;
using Timer = System.Timers.Timer;
#pragma warning disable PendingExcelSchema

namespace Echorama.Helpers;

public unsafe class PanoramaHelper: IDisposable
{
    internal static bool DoingPanorama = false;

    private const int ScreenshotKey = 551;
    private delegate byte IsInputIdClickedDelegate(UIInputData* uiInputData, int key);
    [Signature("E9 ?? ?? ?? ?? 83 7F ?? ?? 0F 8F ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8B CB", DetourName = nameof(IsInputIdClickedDetour))]
    private readonly Hook<IsInputIdClickedDelegate>? IsInputIdClickedHook = null;

    private delegate nint ScreenShotCallbackDelegate(nint a1, int a2);
    [Signature("48 89 5C 24 08 57 48 83 EC 20 BB 8B 07 00 00", DetourName = nameof(ScreenShotCallbackDetour))]
    private readonly Hook<ScreenShotCallbackDelegate>? ScreenShotCallbackHook = null;

    private Configuration configuration;
    private string ptoGenPath;
    private string ptoVarPath;
    private string cpFindPath;
    private string cpCleanPath;
    private string panoModifyPath;
    private string autoOptimiserPath;
    private string nonaPath;
    private string enblendPath;
    private string verdandiPath;

    private bool takeScreenshotPressed;
    private int imageCountH;
    private int imageCountV;
    private double fov;
    private EREventId currentEventId;
    private Vector3 panoramaLocation = Vector3.Zero;
    private uint oldWidth;
    private uint oldHeight;
    private float verticalAngle = 30f;
    private float horizontalAngle = 45f;
    private int anchor = 24;
    private readonly float origMaxVRota;
    private readonly float origMinVRota;
    private float origCamX;

    public PanoramaHelper(Configuration configuration)
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);
        this.configuration = configuration;

        var camera = Common.CameraManager->worldCamera;
        this.origMaxVRota = camera->maxVRotation;
        this.origMinVRota = camera->minVRotation;
        this.configuration.MaxVRota = this.origMaxVRota;
        this.configuration.MinVRota = this.origMinVRota;

        SetupHuginPaths();
    }

    private void SetupHuginPaths()
    {
        if (Dalamud.Utility.Util.GetHostPlatform() == OSPlatform.Windows)
        {
            ptoGenPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "pto_gen.exe");
            ptoVarPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "pto_var.exe");
            cpFindPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "cpfind.exe");
            cpCleanPath = Path.Join(Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName), "bin", "cpclean.exe");
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
        try
        {
            if (!DoingPanorama)
            {
                currentEventId = new EREventId();
                LogHelper.Start(MethodBase.GetCurrentMethod()!.Name, currentEventId);
                LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, "Creating panorama", currentEventId);
                var logMsg = $"VAngle: {verticalAngle}, HAngle: {horizontalAngle}, Anchor: {anchor}";
                MainWindow.ActiveTask(
                    MethodBase.GetCurrentMethod().Name,
                    $"Creating panorama screenshots, hands of mouse and keyboard(or controller)!!!\r\n{logMsg}",
                    currentEventId
                );

                var localPlayer = Plugin.ClientState.LocalPlayer;
                panoramaLocation = localPlayer.Position;

                var weatherId = WeatherManager.Instance()->GetCurrentWeather();
                var weatherSheet = Plugin.DataManager.GetExcelSheet<Weather>(ClientLanguage.English);
                var weatherName = weatherSheet.GetRow(weatherId).Name.ExtractText();
                var eorzeaTime = EorzeanDateTime(Framework.Instance()->ClientTime.EorzeaTime);
                var locationString =
                    $"{panoramaLocation.X.ToString("F0", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Y.ToString("F0", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Z.ToString("F0", CultureInfo.InvariantCulture)}";
                currentEventId.PanoramaPath =
                    Path.Join(configuration.PanoramaFolder, $"{GetTerritoryName()}_{weatherName}_{locationString}_{eorzeaTime}");
                locationString =
                    $"{panoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}";
                currentEventId.PanoramaName =
                    $"{GetTerritoryName()}_{weatherName}_{locationString}_{eorzeaTime}"
                ;

                if (configuration.ShowCharacter)
                {
                    MoveCameraInFrontOfPlayer();
                }

                verticalAngle =
                    180f / (configuration.RowAmount > 1 ? configuration.RowAmount - 1 : configuration.RowAmount);
                horizontalAngle = 360f / configuration.ColumnAmount;
                anchor = configuration.RowAmount > 1
                             ? (configuration.RowAmount - 1) / 2 * configuration.ColumnAmount
                             : 0;

                imageCountH = 0;
                imageCountV = 1;

                var camera = Common.CameraManager->worldCamera;
                camera->mode = 0;

                var device = Device.Instance();
                oldWidth = device->Width;
                oldHeight = device->Height;
                float curWidth = oldWidth;
                float curHeight = oldHeight;
                if (configuration.ScreenshotScale > 1)
                {
                    device->NewWidth = oldWidth * (uint)configuration.ScreenshotScale;
                    device->NewHeight = oldHeight * (uint)configuration.ScreenshotScale;

                    curWidth = device->NewWidth;
                    curHeight = device->NewHeight;
                    device->RequestResolutionChange = 1;
                }
                Timer timer = new Timer(5000);
                timer.Elapsed += (_, __) =>
                {
                    RaptureAtkModule.Instance()->SetUiVisibility(false);
                    DoingPanorama = true;
                    fov = 2 * Math.Atan(Math.Tan(camera->currentFoV / 2.0) * (curWidth / curHeight)) *
                          (180.0 / Math.PI);
                    CalculatePanoramaLogic();
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

    private string GetTerritoryName()
    {
        var territoryRow = Plugin.ClientState.TerritoryType;
        var Territory = Plugin.DataManager.GetExcelSheet<TerritoryType>(ClientLanguage.English)!.GetRow(territoryRow);

        return Territory.PlaceName.Value.Name.ExtractText().Replace("ß", "ss").Replace("ü", "ue").Replace("Ü", "Ue").Replace("ö", "oe").Replace("Ö", "Oe").Replace("ä", "ae").Replace("Ä", "Ae");
    }

    private string EorzeanDateTime(long eorzeanUnixTime)
    {
        var seconds = eorzeanUnixTime;
        var minutes = seconds / 60;
        var bells = minutes / 60;
        var suns = bells / 24;
        var week = suns / 8;
        var moons = week / 4;
        var years = moons / 12;
        return $"{(bells % 24).ToString().PadLeft(2,'0')}{(minutes % 60).ToString().PadLeft(2,'0')}";
    }

    private void DoCameraMovement()
    {
        try
        {
            var camera = Common.CameraManager->worldCamera;
            camera->maxVRotation = Constants.MAXVROTA;
            camera->minVRotation = -Constants.MAXVROTA;
            var hRota = MathF.PI - (MathF.PI / (configuration.ColumnAmount / 2f) * (imageCountH - 1));
            camera->currentHRotation = hRota;
            var vRotaStep = Constants.MAXVROTA / ((configuration.RowAmount - 1) / 2);
            var vRota = configuration.RowAmount > 1 ? Constants.MAXVROTA - (vRotaStep * (imageCountV - 1)) : 0;
            camera->currentVRotation = vRota;

            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Set camera to new angles. VRotation: {vRota} HRotation: {hRota}", currentEventId);
            takeScreenshotPressed = true;
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    private void FinishingTouches()
    {
    }

    public void CalculatePanoramaLogic()
    {
        try
        {
            imageCountH++;
            if (imageCountH > configuration.ColumnAmount)
            {
                imageCountH = 1;
                imageCountV++;
                if (imageCountV > configuration.RowAmount)
                {
                    DoingPanorama = false;
                    var camera = Common.CameraManager->worldCamera;
                    camera->mode = 1;
                    camera->maxVRotation = this.origMaxVRota;
                    camera->minVRotation = this.origMinVRota;
                    RaptureAtkModule.Instance()->SetUiVisibility(true);
                    if (configuration.ScreenshotScale > 1)
                    {
                        var device = Device.Instance();
                        device->NewWidth = oldWidth;
                        device->NewHeight = oldHeight;
                        device->RequestResolutionChange = 1;
                    }

                    StartPanoramaProcess();
                    return;
                }
            }

            DoCameraMovement();
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    private void StartPanoramaProcess()
    {
        try
        {
            Thread thread = new Thread(() =>
            {
                try
                {
                    var threadEventId = currentEventId;
                    var newPanoramaFolder = threadEventId.PanoramaPath;
                    var newPanoramaName = threadEventId.PanoramaName;
                    var tempPanoramaFolder = Path.Join(newPanoramaFolder, "temp");
                    var stitchesPanoramaFolder = Path.Join(newPanoramaFolder, "stitches");
                    var finishedPanoramaFolder = Path.Join(configuration.PanoramaFolder, "finished_panorama");
                    var finishedPanoramaWebpFolder = Path.Join(configuration.PanoramaFolder, "finished_panorama_webp");
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
                        $"-f {fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{Path.Join(tempPanoramaFolder, $"panorama-1.pto")}\" \"{Path.Join(newPanoramaFolder, "images", "row*.jpg")}\"",
                        "pto_gen"
                    );

                    MainWindow.ActiveTask("StartPanoramaProcess | pto_var","Creating panorama of screenshots!\r\nCalling pto_var", threadEventId);
                    int imageCount = 0;
                    float hAngle = 0;
                    float vAngle = 90;
                    for (int y = 0; y < configuration.RowAmount; y++)
                    {
                        for (int x = 0; x < configuration.ColumnAmount; x++)
                        {
                            var tempHAngle = hAngle + (x * horizontalAngle);
                            var tempVAngle = vAngle - (y * verticalAngle);
                            CallCMD(
                                threadEventId,
                                ptoVarPath,
                                $"--set=y{imageCount}={tempHAngle.ToString("F6", CultureInfo.InvariantCulture)},p{imageCount}={tempVAngle.ToString("F6", CultureInfo.InvariantCulture)},v{imageCount}={fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{Path.Join(tempPanoramaFolder, $"panorama{imageCount}.pto")}\" \"{Path.Join(tempPanoramaFolder, $"panorama{imageCount - 1}.pto")}\"",
                                "pto_var"
                            );
                            imageCount++;
                        }
                    }

                    CallCMD(
                        threadEventId,
                        ptoVarPath,
                        $"--modify-opt --opt=y,p,!v --anchor={anchor} --color-anchor={anchor} -o \"{Path.Join(tempPanoramaFolder, $"anchored.pto")}\" \"{Path.Join(tempPanoramaFolder, $"panorama{imageCount - 1}.pto")}\"",
                        "pto_var"
                    );

                    var nextPto = "anchored";
                    if (configuration.ExperimentalCPDetection)
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | cpfind","Creating panorama of screenshots!\r\nCalling cpfind", threadEventId);
                        CallCMD(
                            threadEventId,
                            cpFindPath,
                            $"--prealigned -o \"{Path.Join(tempPanoramaFolder, $"cpfind.pto")}\" \"{Path.Join(tempPanoramaFolder, $"{nextPto}.pto")}\"",
                            "cpfind"
                        );

                        MainWindow.ActiveTask("StartPanoramaProcess | cpclean","Creating panorama of screenshots!\r\nCalling cpclean", threadEventId);
                        CallCMD(
                            threadEventId,
                            cpCleanPath,
                            $"-o \"{Path.Join(tempPanoramaFolder, $"cpclean.pto")}\" \"{Path.Join(tempPanoramaFolder, $"cpfind.pto")}\"",
                            "cpclean"
                        );

                        MainWindow.ActiveTask("StartPanoramaProcess | autooptimiser","Creating panorama of screenshots!\r\nCalling autooptimiser", threadEventId);
                        CallCMD(
                            threadEventId,
                            autoOptimiserPath,
                            $"-a -l -s -v={fov.ToString("F6", CultureInfo.InvariantCulture)}  -o \"{Path.Join(tempPanoramaFolder, $"autooptimiser.pto")}\" \"{Path.Join(tempPanoramaFolder, $"cpclean.pto")}\"",
                            "autooptimiser"
                        );
                        nextPto = "autooptimiser";
                    }

                    var lines = File.ReadAllLines(Path.Join(tempPanoramaFolder, $"{nextPto}.pto"));
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("p "))
                        {
                            lines[i] = ReplaceDimensionsInPLine(lines[i], configuration.PanoramaWidth, configuration.PanoramaHeight);
                            break;
                        }
                    }
                    File.WriteAllLines(Path.Join(tempPanoramaFolder, "resized.pto"), lines);

                    MainWindow.ActiveTask("StartPanoramaProcess | pano_modify","Creating panorama of screenshots!\r\nCalling pano_modify", threadEventId);
                    CallCMD(
                        threadEventId,
                        panoModifyPath,
                        $"--crop=AUTO -o \"{Path.Join(tempPanoramaFolder, "shifted.pto")}\" \"{Path.Join(tempPanoramaFolder, "resized.pto")}\"",
                        "pano_modify"
                    );

                    MainWindow.ActiveTask("StartPanoramaProcess | nona","Creating panorama of screenshots!\r\nCalling nona", threadEventId);
                    CallCMD(
                        threadEventId,
                        nonaPath,
                        $"-o \"{Path.Join(stitchesPanoramaFolder, "stitch")}\" \"{Path.Join(tempPanoramaFolder, "shifted.pto")}\"",
                        "nona"
                    );

                    if (!configuration.MulticoreGen)
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


                    MainWindow.ActiveTask("StartPanoramaProcess | convert","Creating panorama of screenshots!\r\nConverting to webp", threadEventId);
                    ConvertTifToWebp(
                        threadEventId,
                        Path.Join(finishedPanoramaFolder, $"{newPanoramaName}.tif"),
                        Path.Join(finishedPanoramaWebpFolder, $"{newPanoramaName}.webp")
                    );

                    FinishingTouches();
                    MainWindow.ActiveTask("StartPanoramaProcess","DONE!!!", threadEventId);
                    LogHelper.End("StartPanoramaProcess", threadEventId);
                }
                catch (Exception e)
                {
                    LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
                }
            });

            thread.Start();
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    private string ReplaceDimensionsInPLine(string pLine, int newWidth, int newHeight)
    {
        // Findet die w= und h= Teile in der p-Line und ersetzt sie durch die neuen Werte
        string[] parts = pLine.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!configuration.CustomPanoramaDimensions)
            {
                if (parts[i].StartsWith("w") && Convert.ToInt32(parts[i].Replace("w", "")) > 16380)
                {
                    parts[i] = "w" + 16380;
                }
                else if (parts[i].StartsWith("h") && Convert.ToInt32(parts[i].Replace("h", "")) > 8190)
                {
                    parts[i] = "h" + 8190;
                }
            }
            else
            {
                if (parts[i].StartsWith("w"))
                {
                    parts[i] = "w" + configuration.PanoramaWidth;
                }
                else if (parts[i].StartsWith("h"))
                {
                    parts[i] = "h" + configuration.PanoramaHeight;
                }
            }
        }

        // Rekonstruiert die Zeile mit den neuen Werten
        return string.Join(" ", parts);
    }

    internal void ConvertTifToWebp(EREventId eventId ,string tifPath, string webpPath)
    {
        try {
            using (var image = new MagickImage(tifPath))
            {
                image.Format = MagickFormat.WebP;
                image.Depth = 32;
                image.Quality = 95; // verlustbehaftet
                image.Write(webpPath);
            }

        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, eventId);
        }
    }

    private void CallCMD(EREventId eventId ,string exePath, string command, string methodExtra)
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
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Screenshot taken. Row: {imageCountV.ToString().PadLeft(2, '0')} Column: {imageCountH.ToString().PadLeft(2, '0')}", currentEventId);

            MoveLastScreenshot();
            CalculatePanoramaLogic();
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
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }

        return 0;
    }

    private void MoveLastScreenshot()
    {
        try
        {
            var directory = new DirectoryInfo(configuration.ScreenshotFolder);

            FileInfo newScreenshot = directory
                                    .GetFiles()
                                    .OrderByDescending(f => f.LastWriteTime)
                                    .FirstOrDefault();

            var imagesPath = Path.Join(currentEventId.PanoramaPath, "images");
            if (!Directory.Exists($@"{imagesPath}"))
                Directory.CreateDirectory($@"{imagesPath}");

            var screenshotName = $"row{imageCountV.ToString().PadLeft(2, '0')}_col{imageCountH.ToString().PadLeft(2, '0')}.jpg";
            newScreenshot.MoveTo(Path.Join(imagesPath, screenshotName), true);
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Screenshot moved. Row: {imageCountV.ToString().PadLeft(2, '0')} Column: {imageCountH.ToString().PadLeft(2, '0')}", currentEventId);
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }
}
