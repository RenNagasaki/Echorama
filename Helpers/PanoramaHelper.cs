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
    private string newPanoramaFolder = "";
    private float verticalAngle = 30f;
    private float horizontalAngle = 45f;
    private int anchor = 24;

    public PanoramaHelper(Configuration configuration)
    {
        Plugin.GameInteropProvider.InitializeFromAttributes(this);
        this.configuration = configuration;
        var device = Device.Instance();
        oldWidth = device->Width;
        oldHeight = device->Height;
        var camera = Common.CameraManager->worldCamera;
        this.configuration.MaxVRota = camera->maxVRotation;
        this.configuration.MinVRota = camera->minVRotation;
    }
    internal void DoPanorama()
    {
        try
        {
            currentEventId = new EREventId();
            LogHelper.Start(MethodBase.GetCurrentMethod()!.Name, currentEventId);
            LogHelper.Info(MethodBase.GetCurrentMethod()!.Name, "Creating panorama", currentEventId);
            var camera = Common.CameraManager->worldCamera;
            panoramaLocation = Plugin.ClientState.LocalPlayer.Position;
            camera->mode = 0;

            if (configuration.ScreenshotScale > 1)
            {
                var device = Device.Instance();
                device->NewWidth = oldWidth * (uint)configuration.ScreenshotScale;
                device->NewHeight = oldHeight * (uint)configuration.ScreenshotScale;
                device->RequestResolutionChange = 1;
            }

            verticalAngle = 180f / (configuration.RowAmount > 1 ? configuration.RowAmount - 1 : configuration.RowAmount);
            horizontalAngle = 360f / configuration.ColumnAmount;
            anchor = configuration.RowAmount > 1 ? (configuration.RowAmount - 1) / 2 * configuration.ColumnAmount : 0;

            var weatherId = WeatherManager.Instance()->GetCurrentWeather();
            var weatherSheet = Plugin.DataManager.GetExcelSheet<Weather>(ClientLanguage.English);
            weatherName = weatherSheet.GetRow(weatherId).Name.ExtractText();
            eorzeaTime = EorzeanDateTime(Framework.Instance()->ClientTime.EorzeaTime);
            newPanoramaFolder = $@"{configuration.PanoramaFolder}\{GetTerritoryName()}_{weatherName}_{panoramaLocation.X.ToString("F0", CultureInfo.InvariantCulture)}_{panoramaLocation.Y.ToString("F0", CultureInfo.InvariantCulture)}_{panoramaLocation.Z.ToString("F0", CultureInfo.InvariantCulture)}_{eorzeaTime}";
            var logMsg = $"VAngle: {verticalAngle}, HAngle: {horizontalAngle}, Anchor: {anchor}";
            LogHelper.Debug(MethodBase.GetCurrentMethod().Name, logMsg, currentEventId);
            MainWindow.activeTask = $"Creating panorama screenshots, hands of mouse and keyboard(or controller)!!!\r\n{logMsg}";
            imageCountH = 0;
            imageCountV = 1;
            Timer timer = new Timer(5000);
            timer.Elapsed += (_, __) =>
            {
                RaptureAtkModule.Instance()->SetUiVisibility(false);
                DoingPanorama = true;
                fov = 2.0 * Math.Atan(camera->currentFoV) * (180.0 / Math.PI);
                CalculatePanoramaLogic();
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    public static string GetTerritoryName()
    {
        var territoryRow = Plugin.ClientState.TerritoryType;
        var Territory = Plugin.DataManager.GetExcelSheet<TerritoryType>(ClientLanguage.English)!.GetRow(territoryRow);

        return Territory.PlaceName.Value.Name.ExtractText();
    }

    public static string EorzeanDateTime(long eorzeanUnixTime)
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

    public void DoCameraMovement()
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
                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, "Cleaning up temp directory", currentEventId);
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

                MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling pto_gen", currentEventId);
                CallCMD(
                    ptoGenPath,
                    $"-f {fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{newPanoramaFolder}\\temp\\panorama-1.pto\" \"{newPanoramaFolder}\\images\\row*.jpg\"",
                    "pto_gen"
                );

                string[] lines = File.ReadAllLines($"{newPanoramaFolder}\\temp\\panorama-1.pto");

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("p "))
                    {
                        lines[i] = ReplaceDimensionsInPLine(lines[i], 16380, 8190);
                        break;
                    }
                }

                File.WriteAllLines($"{newPanoramaFolder}\\temp\\panorama-1.pto", lines);

                MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling pto_var", currentEventId);
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
                            ptoVarPath,
                            $"--set=y{imageCount}={tempHAngle.ToString("F6", CultureInfo.InvariantCulture)},p{imageCount}={tempVAngle.ToString("F6", CultureInfo.InvariantCulture)},v{imageCount}={fov.ToString("F6", CultureInfo.InvariantCulture)} -o \"{newPanoramaFolder}\\temp\\panorama{imageCount}.pto\" \"{newPanoramaFolder}\\temp\\panorama{imageCount - 1}.pto\"",
                            "pto_var"
                        );
                        imageCount++;
                    }
                }

                CallCMD(
                    ptoVarPath,
                    $"--modify-opt --anchor={anchor} --color-anchor={anchor} --opt=y,p,!v -o \"{newPanoramaFolder}\\temp\\anchored.pto\" \"{newPanoramaFolder}\\temp\\panorama{imageCount - 1}.pto\"",
                    "pto_var"
                );

                if (configuration.ExperimentalCPDetection)
                {
                    MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling cpfind", currentEventId);
                    CallCMD(
                        cpFindPath,
                        $"--prealigned -o \"{newPanoramaFolder}\\temp\\anchored.pto\" \"{newPanoramaFolder}\\temp\\anchored.pto\"",
                        "cpfind"
                    );

                    MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling cpclean", currentEventId);
                    CallCMD(
                        cpCleanPath,
                        $"-o \"{newPanoramaFolder}\\temp\\anchored.pto\" \"{newPanoramaFolder}\\temp\\anchored.pto\"",
                        "cpclean"
                    );

                    MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling autooptimiser", currentEventId);
                    CallCMD(
                        autoOptimiserPath,
                        $"-a -l -s -o \"{newPanoramaFolder}\\temp\\anchored.pto\" \"{newPanoramaFolder}\\temp\\anchored.pto\"",
                        "autooptimiser"
                    );
                }

                MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling pano_modify", currentEventId);
                CallCMD(
                    panoModifyPath,
                    $"--crop=AUTO -o \"{newPanoramaFolder}\\temp\\shifted.pto\" \"{newPanoramaFolder}\\temp\\anchored.pto\"",
                    "pano_modify"
                );

                MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling nona", currentEventId);
                CallCMD(
                    nonaPath,
                    $"-o \"{newPanoramaFolder}\\stitches\\stitch\" \"{newPanoramaFolder}\\temp\\shifted.pto\"",
                    "nona"
                );

                if (!configuration.MulticoreGen)
                {
                    MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling enblend <--- LAST STEP! Takes some time!", currentEventId);
                    CallCMD(
                        enblendPath,
                        $"-o \"{configuration.PanoramaFolder}\\finished_panorama\\{GetTerritoryName()}_{weatherName}_{panoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}_{eorzeaTime}.tif\" \"{newPanoramaFolder}\\stitches\\stitch*.tif\"",
                        "enblend"
                    );
                }
                else
                {
                    MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nCalling verdandi - Takes some time!", currentEventId);
                    CallCMD(
                        verdandiPath,
                        $"-o \"{configuration.PanoramaFolder}\\finished_panorama\\{GetTerritoryName()}_{weatherName}_{panoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}_{eorzeaTime}.tif\" \"{newPanoramaFolder}\\stitches\\stitch*.tif\"",
                        "verdandi"
                    );
                }


                MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"Creating panorama of screenshots!\r\nConverting to webp", currentEventId);
                ConvertTifToWebp(
                    $"{configuration.PanoramaFolder}\\finished_panorama\\{GetTerritoryName()}_{weatherName}_{panoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}_{eorzeaTime}.tif",
                    $"{configuration.PanoramaFolder}\\finished_panorama_webp\\{GetTerritoryName()}_{weatherName}_{panoramaLocation.X.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Y.ToString("F2", CultureInfo.InvariantCulture)}_{panoramaLocation.Z.ToString("F2", CultureInfo.InvariantCulture)}_{eorzeaTime}.webp"
                );

                MainWindow.ActiveTask(MethodBase.GetCurrentMethod()!.Name,"DONE!!!", currentEventId);
                LogHelper.End(MethodBase.GetCurrentMethod()!.Name, currentEventId);
            });

            thread.Start();
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    private static string ReplaceDimensionsInPLine(string pLine, int newWidth, int newHeight)
    {
        // Findet die w= und h= Teile in der p-Line und ersetzt sie durch die neuen Werte
        string[] parts = pLine.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("w"))
            {
                parts[i] = "w" + newWidth;
            }
            else if (parts[i].StartsWith("h"))
            {
                parts[i] = "h" + newHeight;
            }
        }

        // Rekonstruiert die Zeile mit den neuen Werten
        return string.Join(" ", parts);
    }

    internal void ConvertTifToWebp(string tifPath, string webpPath)
    {
        currentEventId = new EREventId();
        //tifPath = "E:\\Echorama-Test\\finished_panorama\\Labyrinthos_Fair Skies_-524.40_68.02_51.51_1607.tif";
        //webpPath = "E:\\Echorama-Test\\finished_panorama_webp\\Labyrinthos_Fair Skies_-524.40_68.02_51.51_1607.webp";
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
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }

    private void CallCMD(string exePath, string command, string methodExtra)
    {
        try
        {
            var process = new Process();
            process.StartInfo.FileName = "cmd.exe"; // oder "bash" unter Linux/macOS
            process.StartInfo.Arguments = @$"/c {exePath} {command}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name + $" | {methodExtra}", @$"Calling command: '{exePath} {command}'", currentEventId);
            process.Start();

            while (!process.HasExited)
            {
                string output = process.StandardOutput.ReadLine();
                LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name + $" | {methodExtra}", output, currentEventId);
            }
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
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

            if (!Directory.Exists($@"{newPanoramaFolder}\images"))
                Directory.CreateDirectory($@"{newPanoramaFolder}\images");

            newScreenshot.MoveTo($@"{newPanoramaFolder}\images\row{imageCountV.ToString().PadLeft(2, '0')}_col{imageCountH.ToString().PadLeft(2, '0')}.jpg", true);
            LogHelper.Debug(MethodBase.GetCurrentMethod()!.Name, $"Screenshot moved. Row: {imageCountV.ToString().PadLeft(2, '0')} Column: {imageCountH.ToString().PadLeft(2, '0')}", currentEventId);
        }
        catch (Exception e)
        {
            LogHelper.Error(MethodBase.GetCurrentMethod()!.Name, e, currentEventId);
        }
    }
}
