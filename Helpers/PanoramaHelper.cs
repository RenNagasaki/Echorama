using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using Echorama.DataClasses;
using Echorama.Windows;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
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
    private string ptoGenPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\pto_gen.exe";
    private string ptoVarPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\pto_var.exe";
    private string cpFindPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\cpfind.exe";
    private string cpCleanPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\cpclean.exe";
    private string panoModifyPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\pano_modify.exe";
    private string autoOptimiserPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\autooptimiser.exe";
    private string nonaPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\nona.exe";
    private string enblendPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\enblend.exe";
    private string verdandiPath = $@"{Path.GetDirectoryName(Plugin.PluginInterface.AssemblyLocation.FullName)}\bin\verdandi.exe";

    private bool takeScreenshotPressed = false;
    private int imageCountH = 0;
    private int imageCountV = 0;
    private double fov = 0;
    private string weatherName = "";
    private string eorzeaTime = "";
    private EREventId currentEventId = null;
    private Vector3 panoramaLocation = Vector3.Zero;
    private uint oldWidth = 0;
    private uint oldHeight = 0;
    private float verticalAngle = 30f;
    private float horizontalAngle = 45f;
    private int anchor = 24;
    private float origMaxVRota = 0f;
    private float origMinVRota = 0f;
    private float origCamX = 0f;

    public PanoramaHelper(Configuration configuration)
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);
        this.configuration = configuration;
        var device = Device.Instance();
        oldWidth = device->Width;
        oldHeight = device->Height;
        var camera = Common.CameraManager->worldCamera;
        this.origMaxVRota = camera->maxVRotation;
        this.origMinVRota = camera->minVRotation;
        this.configuration.MaxVRota = this.origMaxVRota;
        this.configuration.MinVRota = this.origMinVRota;
    }

    internal void MoveCameraInFrontOfPlayer()
    {
        try
        {
            var cameraManager = CameraManager.Instance();
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
            camera->x += 100f;
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
                var camera = Common.CameraManager->worldCamera;
                var localPlayer = Plugin.ClientState.LocalPlayer;
                panoramaLocation = localPlayer.Position;
                camera->mode = 0;

                if (configuration.ShowCharacter)
                {
                    MoveCameraInFrontOfPlayer();
                }

                float curWidth = oldWidth;
                float curHeight = oldHeight;
                if (configuration.ScreenshotScale > 1)
                {
                    var device = Device.Instance();
                    device->NewWidth = oldWidth * (uint)configuration.ScreenshotScale;
                    device->NewHeight = oldHeight * (uint)configuration.ScreenshotScale;

                    curWidth = device->NewWidth;
                    curHeight = device->NewHeight;
                    device->RequestResolutionChange = 1;
                }

                verticalAngle =
                    180f / (configuration.RowAmount > 1 ? configuration.RowAmount - 1 : configuration.RowAmount);
                horizontalAngle = 360f / configuration.ColumnAmount;
                anchor = configuration.RowAmount > 1
                             ? (configuration.RowAmount - 1) / 2 * configuration.ColumnAmount
                             : 0;

                var weatherId = WeatherManager.Instance()->GetCurrentWeather();
                var weatherSheet = Plugin.DataManager.GetExcelSheet<Weather>(ClientLanguage.English);
                weatherName = weatherSheet.GetRow(weatherId).Name.ExtractText();
                eorzeaTime = EorzeanDateTime(Framework.Instance()->ClientTime.EorzeaTime);
                var locationString =
                    $"{panoramaLocation.X.ToString("F0", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Y.ToString("F0", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Z.ToString("F0", CultureInfo.InvariantCulture)}";
                currentEventId.PanoramaPath =
                    $@"{configuration.PanoramaFolder}\{GetTerritoryName()}_{weatherName}_{locationString}_{eorzeaTime}";
                locationString =
                    $"{panoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_" +
                    $"{panoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}";
                currentEventId.PanoramaName =
                    $"{GetTerritoryName()}_{weatherName}_{locationString}_{eorzeaTime}"
                ;
                var logMsg = $"VAngle: {verticalAngle}, HAngle: {horizontalAngle}, Anchor: {anchor}";
                LogHelper.Debug(MethodBase.GetCurrentMethod().Name, logMsg, currentEventId);
                MainWindow.activeTask =
                    $"Creating panorama screenshots, hands of mouse and keyboard(or controller)!!!\r\n{logMsg}";
                imageCountH = 0;
                imageCountV = 1;
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
                    LogHelper.Debug("StartPanoramaProcess", "Cleaning up temp directory", threadEventId);
                    if (!Directory.Exists($@"{newPanoramaFolder}"))
                        Directory.CreateDirectory($@"{newPanoramaFolder}");
                    if (!Directory.Exists($@"{newPanoramaFolder}\temp"))
                        Directory.CreateDirectory($@"{newPanoramaFolder}\temp");
                    if (!Directory.Exists($@"{newPanoramaFolder}\stitches"))
                        Directory.CreateDirectory($@"{newPanoramaFolder}\stitches");
                    if (!Directory.Exists($@"{configuration.PanoramaFolder}\finished_panorama"))
                        Directory.CreateDirectory($@"{configuration.PanoramaFolder}\finished_panorama");
                    if (!Directory.Exists($@"{configuration.PanoramaFolder}\finished_panorama_webp"))
                        Directory.CreateDirectory($@"{configuration.PanoramaFolder}\finished_panorama_webp");

                    MainWindow.ActiveTask("StartPanoramaProcess | pto_gen","Creating panorama of screenshots!\r\nCalling pto_gen", threadEventId);
                    CallCMD(
                        threadEventId,
                        ptoGenPath,
                        $"-f {fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{newPanoramaFolder}\\temp\\panorama-1.pto\" \"{newPanoramaFolder}\\images\\row*.jpg\"",
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
                                $"--set=y{imageCount}={tempHAngle.ToString("F6", CultureInfo.InvariantCulture)},p{imageCount}={tempVAngle.ToString("F6", CultureInfo.InvariantCulture)},v{imageCount}={fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{newPanoramaFolder}\\temp\\panorama{imageCount}.pto\" \"{newPanoramaFolder}\\temp\\panorama{imageCount - 1}.pto\"",
                                "pto_var"
                            );
                            imageCount++;
                        }
                    }

                    CallCMD(
                        threadEventId,
                        ptoVarPath,
                        $"--modify-opt --opt=y,p,!v --anchor={anchor} --color-anchor={anchor} -o \"{newPanoramaFolder}\\temp\\anchored.pto\" \"{newPanoramaFolder}\\temp\\panorama{imageCount - 1}.pto\"",
                        "pto_var"
                    );

                    var nextPto = "anchored";
                    if (configuration.ExperimentalCPDetection)
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | cpfind","Creating panorama of screenshots!\r\nCalling cpfind", threadEventId);
                        CallCMD(
                            threadEventId,
                            cpFindPath,
                            $"--prealigned -o \"{newPanoramaFolder}\\temp\\cpfind.pto\" \"{newPanoramaFolder}\\temp\\{nextPto}.pto\"",
                            "cpfind"
                        );

                        MainWindow.ActiveTask("StartPanoramaProcess | cpclean","Creating panorama of screenshots!\r\nCalling cpclean", threadEventId);
                        CallCMD(
                            threadEventId,
                            cpCleanPath,
                            $"-o \"{newPanoramaFolder}\\temp\\cpclean.pto\" \"{newPanoramaFolder}\\temp\\cpfind.pto\"",
                            "cpclean"
                        );

                        MainWindow.ActiveTask("StartPanoramaProcess | autooptimiser","Creating panorama of screenshots!\r\nCalling autooptimiser", threadEventId);
                        CallCMD(
                            threadEventId,
                            autoOptimiserPath,
                            $"-a -l -s -v={fov.ToString("F6", CultureInfo.InvariantCulture)}  -o \"{newPanoramaFolder}\\temp\\optimised.pto\" \"{newPanoramaFolder}\\temp\\cpclean.pto\"",
                            "autooptimiser"
                        );
                        nextPto = "optimised";
                    }

                    var lines = File.ReadAllLines($"{newPanoramaFolder}\\temp\\{nextPto}.pto");
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].StartsWith("p "))
                        {
                            lines[i] = ReplaceDimensionsInPLine(lines[i], configuration.PanoramaWidth, configuration.PanoramaHeight);
                            break;
                        }
                    }
                    File.WriteAllLines($"{newPanoramaFolder}\\temp\\resized.pto", lines);

                    MainWindow.ActiveTask("StartPanoramaProcess | pano_modify","Creating panorama of screenshots!\r\nCalling pano_modify", threadEventId);
                    CallCMD(
                        threadEventId,
                        panoModifyPath,
                        $"--crop=AUTO -o \"{newPanoramaFolder}\\temp\\shifted.pto\" \"{newPanoramaFolder}\\temp\\resized.pto\"",
                        "pano_modify"
                    );

                    MainWindow.ActiveTask("StartPanoramaProcess | nona","Creating panorama of screenshots!\r\nCalling nona", threadEventId);
                    CallCMD(
                        threadEventId,
                        nonaPath,
                        $"-o \"{newPanoramaFolder}\\stitches\\stitch\" \"{newPanoramaFolder}\\temp\\shifted.pto\"",
                        "nona"
                    );

                    if (!configuration.MulticoreGen)
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | enblend","Creating panorama of screenshots!\r\nCalling enblend <--- LAST STEP! Takes some time!", threadEventId);
                        CallCMD(
                            threadEventId,
                            enblendPath,
                            $"-o \"{configuration.PanoramaFolder}\\finished_panorama\\{newPanoramaName}.tif\" \"{newPanoramaFolder}\\stitches\\stitch*.tif\"",
                            "enblend"
                        );
                    }
                    else
                    {
                        MainWindow.ActiveTask("StartPanoramaProcess | verdandi","Creating panorama of screenshots!\r\nCalling verdandi - Takes some time!", threadEventId);
                        CallCMD(
                            threadEventId,
                            verdandiPath,
                            $"-o \"{configuration.PanoramaFolder}\\finished_panorama\\{newPanoramaName}.tif\" \"{newPanoramaFolder}\\stitches\\stitch*.tif\"",
                            "verdandi"
                        );
                    }


                    MainWindow.ActiveTask("StartPanoramaProcess | convert","Creating panorama of screenshots!\r\nConverting to webp", threadEventId);
                    ConvertTifToWebp(
                        threadEventId,
                        $"{configuration.PanoramaFolder}\\finished_panorama\\{newPanoramaName}.tif",
                        $"{configuration.PanoramaFolder}\\finished_panorama_webp\\{newPanoramaName}.webp"
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
            process.StartInfo.FileName = "cmd.exe"; // oder "bash" unter Linux/macOS
            process.StartInfo.Arguments = @$"/c {exePath} {command}";
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

            if (!Directory.Exists($@"{currentEventId.PanoramaPath}\images"))
                Directory.CreateDirectory($@"{currentEventId.PanoramaPath}\images");

            newScreenshot.MoveTo($@"{currentEventId.PanoramaPath}\images\row{imageCountV.ToString().PadLeft(2, '0')}_col{imageCountH.ToString().PadLeft(2, '0')}.jpg", true);
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Screenshot moved. Row: {imageCountV.ToString().PadLeft(2, '0')} Column: {imageCountH.ToString().PadLeft(2, '0')}", currentEventId);
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }
}
