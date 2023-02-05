﻿using System.Diagnostics;
using OpenCvSharp;
using OBSWebsocketDotNet;
using System.Text;

var dir = Directory.GetCurrentDirectory();

const string visualiserWindowName = "CV";

const int width = 1920;
const int height = 1080;

const double gearSimilarityThreshold = 0.7d;
const double vehicleSimilarityThreshold = 0.7d;
const int jpegQuality = 80;

const string obsWebsocket = "ws://127.0.0.1:4455";

var wsClient = new OBSWebsocket();
var font = HersheyFonts.HersheyPlain;

wsClient.ConnectAsync(obsWebsocket, string.Empty);

while(!wsClient.IsConnected)
{
    Console.WriteLine($"Waiting for OBS connection {obsWebsocket}]");
    await Task.Delay(1000);
}

Console.WriteLine("Connected to OBS!");

const int gearResize = 16;
var gearImage = Cv2.ImRead(dir + "/templates/gear.png");
using var gearImageGray = new Mat();
Cv2.CvtColor(gearImage, gearImageGray, ColorConversionCodes.BGR2GRAY);

var statsBuilder = new StringBuilder();

var cycleTimer = new Stopwatch();
var localTimer = new Stopwatch();

var showStats = true;
var openVisualiser = true;

var vehicleTemplates = new string[] {
    "blackbird_wide_dark",
    "hovercraft_wide_dark"
};

var vehicleMatsGray = new Mat[vehicleTemplates.Length];
for (var i = 0; i < vehicleMatsGray.Length; i++)
{
    vehicleMatsGray[i] = new Mat();
    using var img = Cv2.ImRead(dir + "/templates/" + vehicleTemplates[i] + ".png");
    Cv2.CvtColor(img, vehicleMatsGray[i], ColorConversionCodes.BGR2GRAY);
    Console.WriteLine($"Vehicle template ready: {vehicleTemplates[i]}");
}

if (openVisualiser)
{
    Cv2.NamedWindow(visualiserWindowName, WindowFlags.AutoSize | WindowFlags.GuiNormal);
    Console.WriteLine("Starting CV visualiser");
}

while (true)
{
    cycleTimer.Restart();
    localTimer.Restart();

    // Capture compressed jpg, resize to 1080p
    var screen64 = wsClient.GetSourceScreenshot("Capture", "jpg", width, height, jpegQuality).Replace("data:image/jpg;base64,", string.Empty);
    statsBuilder.AppendLine($"Capture download: {localTimer.ElapsedMilliseconds}ms");
    localTimer.Restart();

    // Decode base64 buffer
    var screenBuffer = Convert.FromBase64String(screen64);
    statsBuilder.AppendLine($"Capture decode: {localTimer.ElapsedMilliseconds} ms");
    localTimer.Restart();

    // Decode capture to CV2 array
    using var screenDecoded = Cv2.ImDecode(screenBuffer, ImreadModes.Color);
    statsBuilder.AppendLine($"CV2 Decode: {localTimer.ElapsedMilliseconds} ms");
    localTimer.Restart();

    // Convert capture to grayscale
    using var screenGray = new Mat();
    Cv2.CvtColor(screenDecoded, screenGray, ColorConversionCodes.BGR2GRAY);
    statsBuilder.AppendLine($"CV2 Grayscale: {localTimer.ElapsedMilliseconds} ms");
    localTimer.Restart();

    // Run template (gear) matching against capture
    using var gearResult = new Mat();
    Cv2.MatchTemplate(gearImageGray, screenGray, gearResult, TemplateMatchModes.CCoeffNormed);
    statsBuilder.AppendLine($"CV2 MatchTemplate(gear): {localTimer.ElapsedMilliseconds} ms");
    localTimer.Restart();

    // Gear similarity score and best position
    Point gearMaxLoc;
    double gearSimiliartyScore;
    Cv2.MinMaxLoc(gearResult, out _, out gearSimiliartyScore, out _, out gearMaxLoc);

    var showVehicles = true;
    if (showVehicles)
    {
        using var vehicleResult = new Mat();
        for(var i = 0; i < vehicleMatsGray.Length; i++)
        {
            localTimer.Restart();

            var vehicleGray = vehicleMatsGray[i];
            var vehicleName = vehicleTemplates[i];

            Cv2.MatchTemplate(vehicleGray, screenGray, vehicleResult, TemplateMatchModes.CCoeffNormed);

            Point vehiclePos;
            double vehicleScore;
            Cv2.MinMaxLoc(vehicleResult, out _, out vehicleScore, out _, out vehiclePos);

            if (vehicleScore > vehicleSimilarityThreshold)
            {
                Cv2.Rectangle(screenDecoded, new Rect(vehiclePos, vehicleGray.Size()), Scalar.Red, 2);
                Cv2.PutText(screenDecoded, $"{vehicleName}:{vehicleScore}", vehiclePos.Add(new(0, -15)), HersheyFonts.HersheySimplex, 0.4d, Scalar.Red, lineType: LineTypes.AntiAlias);
            }

            statsBuilder.AppendLine($"CV2 Full({vehicleName}): {localTimer.ElapsedMilliseconds} ms");
        }
    }

    if (openVisualiser)
    {
        if (gearSimiliartyScore > 0.5d)
        {
            // Adjust point to include area around the gear
            gearMaxLoc.X -= gearResize / 2;
            gearMaxLoc.Y -= gearResize / 2;

            var newSize = gearImage.Size();
            newSize.Width += gearResize;
            newSize.Height += gearResize;

            var similarityColor = gearSimiliartyScore > gearSimilarityThreshold ? Scalar.LimeGreen : Scalar.OrangeRed;
            var textLoc = gearMaxLoc.Subtract(new(0, 10));

            // Visualise best match
            Cv2.Rectangle(screenDecoded, new Rect(gearMaxLoc, newSize), similarityColor, 2);
            Cv2.PutText(screenDecoded, $"{gearSimiliartyScore}", textLoc, HersheyFonts.HersheySimplex, 0.4d, similarityColor, lineType: LineTypes.AntiAlias);
        }

        // Show render stats
        if (showStats)
        {
            statsBuilder.Append($"Current Cycle: {cycleTimer.ElapsedMilliseconds} ms");

            var statsPosition = new Point(100, 100);
            var text = statsBuilder.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach(var line in text)
            {
                Cv2.PutText(screenDecoded, line, statsPosition, font, 1.5d, Scalar.Black, thickness: 4, lineType: LineTypes.AntiAlias);
                Cv2.PutText(screenDecoded, line, statsPosition, font, 1.5d, Scalar.White, lineType: LineTypes.AntiAlias);
                statsPosition = statsPosition.Add(new(0, 22));
            }
        }

        Cv2.ImShow(visualiserWindowName, screenDecoded);
        Cv2.WaitKey(1);

        statsBuilder.Clear();
    }
}