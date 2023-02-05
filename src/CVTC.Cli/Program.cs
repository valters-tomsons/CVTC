using System.Diagnostics;
using System.Text;
using CVTC.Windows;
using OBSWebsocketDotNet;
using OpenCvSharp;

var dir = Directory.GetCurrentDirectory();

const string visualiserWindowName = "CVTC Visualization";

const int width = 1920;
const int height = 1080;

const double gearSimilarityThreshold = 0.725d;
const double vehicleSimilarityThreshold = 0.65d;
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
using var gearImageGray = new Mat();
Cv2.CvtColor(gearImage, gearImageGray, ColorConversionCodes.BGR2GRAY);

var showVisualizer = false;
var trackStats = false;

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

var motionFactory = NaturalMouseMotionSharp.Util.FactoryTemplates.CreateFastGamerMotionFactory();

Task? currentMotion = null;
// var state = CVTC.

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
var vehicleMenuFound = false;
var vehicleReadyClicked = false;

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
    using var screenDecoded = Cv2.ImDecode(screenBuffer, ImreadModes.Color);
    if (trackStats) statsStrBuilder!.AppendLine($"CV2 Decode: {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Convert capture to grayscale
    using var screenGray = new Mat();
    Cv2.CvtColor(screenDecoded, screenGray, ColorConversionCodes.BGR2GRAY);
    if (trackStats) statsStrBuilder!.AppendLine($"CV2 Grayscale: {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Run template (gear) matching against capture
    using var gearResult = new Mat();
    Cv2.MatchTemplate(gearImageGray, screenGray, gearResult, TemplateMatchModes.CCoeffNormed);
    if (trackStats) statsStrBuilder!.AppendLine($"CV2 MatchTemplate(gear): {localTimer!.ElapsedMilliseconds} ms");
    localTimer?.Restart();

    // Gear similarity score and best position
    Point gearPos;
    double gearSimiliartyScore;
    Cv2.MinMaxLoc(gearResult, out _, out gearSimiliartyScore, out _, out gearPos);

    if (gearSimiliartyScore >= gearSimilarityThreshold)
    {
        if (!vehicleMenuFound && MoveMouseAllowed() && (currentMotion is null || currentMotion?.IsCompleted == true))
        {
            var mouseTargetX = gearPos.X + gearImage.Size().Width / 2;
            var mouseTargetY = gearPos.Y + gearImage.Size().Height / 2;
            vehicleMenuFound = true;
            currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null).ContinueWith(x =>
            {
                User32Interop.LeftClick();
            });
        }
    }

    if (currentMotion is not null)
    {
        await currentMotion;
    }

    if (vehicleMenuFound && !vehicleReadyClicked)
    {
        localTimer?.Restart();

        using var lightVehicleResult = new Mat();
        using var darkVehicleResult = new Mat();
        Cv2.MatchTemplate(lightTemplateImg, screenGray, lightVehicleResult, TemplateMatchModes.CCoeffNormed);
        Cv2.MatchTemplate(darkTemplateImg, screenGray, darkVehicleResult, TemplateMatchModes.CCoeffNormed);

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
            Cv2.Rectangle(screenDecoded, new Rect(lightVehiclePos, lightTemplateImg.Size()), lightVehicleColor, 2);
            Cv2.PutText(screenDecoded, $"{lightTemplateName}:{lightVehicleScore}", lightVehiclePos.Add(new(0, -15)), HersheyFonts.HersheySimplex, 0.4d, lightVehicleColor, lineType: LineTypes.AntiAlias);

            var darkVehicleColor = darkVehicleMatch ? Scalar.LimeGreen : Scalar.OrangeRed;
            Cv2.Rectangle(screenDecoded, new Rect(darkVehiclePos, darkTemplateImg.Size()), darkVehicleColor, 2);
            Cv2.PutText(screenDecoded, $"{darkTemplateName}:{darkVehicleScore}", darkVehiclePos.Add(new(0, -15)), HersheyFonts.HersheySimplex, 0.4d, darkVehicleColor, lineType: LineTypes.AntiAlias);
        }

        if (lightVehicleMatch || darkVehicleMatch)
        {
            var bestMatch = Math.Max(lightVehicleScore, darkVehicleScore);

            if (lightVehicleScore == bestMatch)
            {
                if (MoveMouseAllowed() && (currentMotion is null || currentMotion?.IsCompleted == true))
                {
                    var mouseTargetX = lightVehiclePos.X + lightTemplateImg.Size().Width / 2;
                    var mouseTargetY = lightVehiclePos.Y + lightTemplateImg.Size().Height / 2;

                    currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null).ContinueWith(x =>
                    {
                        User32Interop.LeftClick();
                    });
                    vehicleReadyClicked = true;
                }
            }
            else if (darkVehicleScore == bestMatch)
            {
                if (MoveMouseAllowed() && (currentMotion is null || currentMotion?.IsCompleted == true))
                {
                    var mouseTargetX = darkVehiclePos.X + darkTemplateImg.Size().Width / 2;
                    var mouseTargetY = darkVehiclePos.Y + darkTemplateImg.Size().Height / 2;
                    currentMotion = motionFactory.MoveAsync(mouseTargetX, mouseTargetY, null);
                }
            }
        }
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
            statsStrBuilder!.AppendLine($"vehicleMenuFound: {vehicleMenuFound}");
            statsStrBuilder!.AppendLine($"vehicleReadyClicked: {vehicleReadyClicked}");

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