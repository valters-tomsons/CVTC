using System.Diagnostics;
using System.Text;
using CVTC.Windows;
using CVTC.Enums;
using OBSWebsocketDotNet;
using OpenCvSharp;

var dir = Directory.GetCurrentDirectory();

const string visualiserWindowName = "CVTC Visualization";

const int width = 1920;
const int height = 1080;

const double gearSimilarityThreshold = 0.93d;
const double vehicleSimilarityThreshold = 0.989d;
const int jpegQuality = 80;

const string obsWebsocket = "ws://127.0.0.1:4455";
const string targetWindowTitle = "Battlefield";

var wsClient = new OBSWebsocket();
var font = HersheyFonts.HersheyPlain;

wsClient.ConnectAsync(obsWebsocket, string.Empty);

while (!wsClient.IsConnected)
{
    Console.WriteLine($"Waiting for OBS connection {obsWebsocket}]");
    await Task.Delay(1000);
}

Console.WriteLine("Connected to OBS!");

const int gearResize = 16;
var gearImage = Cv2.ImRead(dir + "/templates/gear.png");
using var gearImageGray = new UMat();
Cv2.CvtColor(gearImage, gearImageGray, ColorConversionCodes.BGR2GRAY);

var showVisualizer = true;
var trackStats = true;

var statsStrBuilder = trackStats ? new StringBuilder() : null!;

var cycleTimer = trackStats ? new Stopwatch() : null;
var localTimer = trackStats ? new Stopwatch() : null;

// var templateName = "blackbird_wide_dark";
var lightTemplateName = "blackbird_wide_light";
var darkTemplateName = "blackbird_wide_dark";
using var lightTemplateImg = Cv2.ImRead(dir + "/templates/" + lightTemplateName + ".png", ImreadModes.Grayscale);
using var darkTemplateImg = Cv2.ImRead(dir + "/templates/" + darkTemplateName + ".png", ImreadModes.Grayscale);

if (showVisualizer)
{
    Cv2.NamedWindow(visualiserWindowName, WindowFlags.AutoSize);
}

var motionFactory = NaturalMouseMotionSharp.Util.FactoryTemplates.CreateDemoRobotMotionFactory(50);
Task? currentMotion = null;

bool MoveMouseAllowed()
{
    if (currentMotion?.IsCompleted == false)
    {
        return false;
    }

    var title = User32Interop.GetActiveWindowTitle();
    if (!title.Contains(targetWindowTitle))
    {
        return false;
    }

    return true;
}

var vehicleMenuIconClicked = false;
var vehicleSelected = false;

var vehicleRetries = 0;

var gearPos = new Point();
var gearSimiliartyScore = 0d;

var state = BFState.Unknown;

async Task ProcessGearTemplate()
{
    if (!vehicleMenuIconClicked && MoveMouseAllowed() && (currentMotion is null || currentMotion?.IsCompleted == true))
    {
        var mouseTargetX = gearPos.X + gearImage.Size().Width / 2;
        var mouseTargetY = gearPos.Y + gearImage.Size().Height / 2;
        vehicleMenuIconClicked = true;

        currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null).ContinueWith(x =>
        {
            User32Interop.LeftClick();
            state = BFState.MatchVehiclesMenu;
        });
    }
}

async Task ProcessVehicleIconTemplates(UMat grayscale)
{
    localTimer?.Restart();

    using var lightVehicleResult = new UMat();
    using var darkVehicleResult = new UMat();
    Cv2.MatchTemplate(lightTemplateImg, grayscale, lightVehicleResult, TemplateMatchModes.CCorrNormed);
    Cv2.MatchTemplate(darkTemplateImg, grayscale, darkVehicleResult, TemplateMatchModes.CCorrNormed);

    Point lightVehiclePos, darkVehiclePos;
    double lightVehicleScore, darkVehicleScore;

    Cv2.MinMaxLoc(lightVehicleResult, out _, out lightVehicleScore, out _, out lightVehiclePos);
    Cv2.MinMaxLoc(darkVehicleResult, out _, out darkVehicleScore, out _, out darkVehiclePos);

    var lightVehicleMatch = lightVehicleScore >= vehicleSimilarityThreshold;
    var darkVehicleMatch = darkVehicleScore >= vehicleSimilarityThreshold;

    if (trackStats) statsStrBuilder!.AppendLine($"CV2 Templates: {localTimer!.ElapsedMilliseconds} ms");
    if (showVisualizer)
    {
        var lightVehicleColor = lightVehicleMatch ? Scalar.LimeGreen : Scalar.OrangeRed;
        Cv2.Rectangle(grayscale, new Rect(lightVehiclePos, lightTemplateImg.Size()), lightVehicleColor, 2);
        Cv2.PutText(grayscale, $"{lightTemplateName}:{lightVehicleScore}", lightVehiclePos.Add(new(0, -15)), HersheyFonts.HersheySimplex, 0.4d, lightVehicleColor, lineType: LineTypes.AntiAlias);

        var darkVehicleColor = darkVehicleMatch ? Scalar.LimeGreen : Scalar.OrangeRed;
        Cv2.Rectangle(grayscale, new Rect(darkVehiclePos, darkTemplateImg.Size()), darkVehicleColor, 2);
        Cv2.PutText(grayscale, $"{darkTemplateName}:{darkVehicleScore}", darkVehiclePos.Add(new(0, -15)), HersheyFonts.HersheySimplex, 0.4d, darkVehicleColor, lineType: LineTypes.AntiAlias);
    }

    if (lightVehicleMatch || darkVehicleMatch)
    {
        var bestMatch = Math.Max(lightVehicleScore, darkVehicleScore);

        if (lightVehicleScore == bestMatch)
        {
            if (MoveMouseAllowed() && (currentMotion is null || currentMotion?.IsCompleted == true))
            {
                Trace.WriteLine("Vehicle found; available, invoking motion & click");

                var mouseTargetX = lightVehiclePos.X + lightTemplateImg.Size().Width / 2;
                var mouseTargetY = lightVehiclePos.Y + lightTemplateImg.Size().Height / 2;

                vehicleSelected = true;

                currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null).ContinueWith(x =>
                {
                    User32Interop.LeftClick();
                    state = BFState.VehicleAvailable;
                });
            }
        }
        else if (darkVehicleScore == bestMatch)
        {
            if (MoveMouseAllowed() && (currentMotion is null || currentMotion?.IsCompleted == true))
            {
                var mouseTargetX = darkVehiclePos.X + darkTemplateImg.Size().Width / 2;
                var mouseTargetY = darkVehiclePos.Y + darkTemplateImg.Size().Height / 2;
                currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null);

                vehicleRetries++;
                state = BFState.VehicleUnavailable;

                await Task.Delay(50);
            }
        }
    }
}

async Task RefreshVehicleMenu()
{
    Trace.WriteLine("Vehicle unavailable, resetting cycle");

    var mouseTargetX = gearPos.X + gearImage.Size().Width / 2;
    var mouseTargetY = gearPos.Y + gearImage.Size().Height / 2;

    currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null).ContinueWith(x =>
    {
        User32Interop.LeftClick();
        state = BFState.MatchOverview;
    });

    vehicleMenuIconClicked = false;
    vehicleRetries = 0;
}

while (true)
{
    cycleTimer?.Restart();
    localTimer?.Restart();

    // Capture compressed jpg, resize to 1080p
    var screen64 = wsClient.GetSourceScreenshot("Capture", "jpg", width, height, jpegQuality).Replace("data:image/jpg;base64,", string.Empty);
    if (trackStats) statsStrBuilder!.AppendLine($"Capture download: {localTimer!.ElapsedMilliseconds}ms");
    localTimer?.Restart();

    // Decode base64 buffer
    var screenBuffer = Convert.FromBase64String(screen64);
    if (trackStats) statsStrBuilder!.AppendLine($"Capture decode: {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Decode capture to CV2 array
    Cv2.ImDecode(screenBuffer, ImreadModes.Color);
    using var screenDecoded = Cv2.ImDecode(screenBuffer, ImreadModes.Color);
    if (trackStats) statsStrBuilder!.AppendLine($"CV2 Decode: {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Convert capture to grayscale
    using var screenGray = new UMat();
    Cv2.CvtColor(screenDecoded, screenGray, ColorConversionCodes.BGR2GRAY);
    if (trackStats) statsStrBuilder!.AppendLine($"CV2 Grayscale: {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Run template (gear) matching against capture
    using var gearResult = new UMat();
    Cv2.MatchTemplate(gearImageGray, screenGray, gearResult, TemplateMatchModes.CCorrNormed);
    if (trackStats) statsStrBuilder!.AppendLine($"CV2 MatchTemplate(gear): {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Gear similarity score and best position
    Cv2.MinMaxLoc(gearResult, out _, out gearSimiliartyScore, out _, out gearPos);

    if (gearSimiliartyScore >= gearSimilarityThreshold)
    {
        await ProcessGearTemplate();
    }

    if (currentMotion is not null)
    {
        await currentMotion;
    }

    if (vehicleRetries > 5 && vehicleMenuIconClicked && !vehicleSelected)
    { 
        await RefreshVehicleMenu();
    }

    if (vehicleMenuIconClicked && !vehicleSelected)
    {
        await ProcessVehicleIconTemplates(screenGray);
    }

    if (showVisualizer)
    {
        if (gearSimiliartyScore > 0.5d)
        {
            // Adjust point to include area around the gear
            gearPos.Y -= gearResize / 2;
            gearPos.X -= gearResize / 2;

            var newSize = gearImage.Size();
            newSize.Width += gearResize;
            newSize.Height += gearResize;

            var gearColor = gearSimiliartyScore > gearSimilarityThreshold ? Scalar.LimeGreen : Scalar.OrangeRed;
            var textLoc = gearPos.Subtract(new(0, 10));

            // Move label below icon, if on top of the screen
            if (textLoc.Y < 20)
            {
                textLoc.Y += gearImage.Size().Height + (gearResize * 2) + 10;
            }

            // Visualise best match
            Cv2.Rectangle(screenDecoded, new Rect(gearPos, newSize), gearColor, 2);
            Cv2.PutText(screenDecoded, $"{gearSimiliartyScore}", textLoc, HersheyFonts.HersheySimplex, 0.4d, gearColor, lineType: LineTypes.AntiAlias);
        }

        // Show render stats
        if (trackStats)
        {
            statsStrBuilder!.AppendLine($"Current Cycle: {cycleTimer!.ElapsedMilliseconds} ms");
            statsStrBuilder!.AppendLine($"vehicleMenuFound: {vehicleMenuIconClicked}");
            statsStrBuilder!.AppendLine($"vehicleReadyClicked: {vehicleSelected}");

            var statsPosition = new Point(100, 100);
            var text = statsStrBuilder!.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var line in text)
            {
                Cv2.PutText(screenDecoded, line, statsPosition, font, 1.5d, Scalar.Black, thickness: 4, lineType: LineTypes.AntiAlias);
                Cv2.PutText(screenDecoded, line, statsPosition, font, 1.5d, Scalar.White, lineType: LineTypes.AntiAlias);
                statsPosition = statsPosition.Add(new(0, 22));
            }
        }

        Cv2.ImShow(visualiserWindowName, screenDecoded);
        Cv2.WaitKey(1);

        statsStrBuilder?.Clear();
    }
}