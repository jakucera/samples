using PlayFab;
using PlayFab.AdminModels;

// Read required input for segment export
Console.Write("TitleId: ");
var titleId = Console.ReadLine();

Console.Write("SegmentId: ");
var segmentId = Console.ReadLine();

Console.Write("Secret Key: ");
var secretKey = Console.ReadLine();

Console.Write("Segment Export Id (leave blank to start a new export):");
var exportId = Console.ReadLine();

Console.Write("Combine output files (y/N):");
var combine = Console.ReadLine();
bool combineFiles = string.Compare(combine, "y", ignoreCase: true) == 0 
    || string.Compare(combine, "yes", ignoreCase: true) == 0;

Console.Write(combineFiles ? "Output File Path: " : "Output Folder Path: ");
var outputPath = Console.ReadLine();
if (string.IsNullOrEmpty(outputPath))
{
    Console.WriteLine("Invalid path");
    return;
}

// Set PlayFab API settings
PlayFabSettings.staticSettings.TitleId = titleId;
PlayFabSettings.staticSettings.DeveloperSecretKey = secretKey;

// If no exportId was provided then start a new segment export with ExportPlayersInSegmentAsync
if (string.IsNullOrEmpty(exportId))
{
    var exportRequest = new ExportPlayersInSegmentRequest
    {
        SegmentId = segmentId
    };

    var exportResponse = await PlayFabAdminAPI.ExportPlayersInSegmentAsync(exportRequest);

    if (exportResponse.Error != null)
    {
        Console.WriteLine(exportResponse.Error);
        return;
    }

    exportId = exportResponse.Result.ExportId;
    Console.WriteLine($"Export started for segment '{segmentId}' in title '{titleId}'. Export id = '{exportId}'.");
}

// Check the status of the segment export until the export is complete
var exportStatusRequest = new GetPlayersInSegmentExportRequest
    {
        ExportId = exportId
    };

PlayFabResult<GetPlayersInSegmentExportResponse> exportStatusResponse;

do
{
    Console.WriteLine($"Checking export status.");

    exportStatusResponse = await PlayFabAdminAPI.GetSegmentExportAsync(exportStatusRequest);
    Console.WriteLine($"Export status is '{exportStatusResponse.Result.State}'.");

    if (exportStatusResponse.Result.State == "Expired")
    {
        Console.WriteLine($"Export '{exportId}' is expired. Please call ExportPlayersInSegmentAsync to start a new segment export.");
        return;
    }

    if (exportStatusResponse.Result.State == "NotFound")
    {
        Console.WriteLine($"Export '{exportId}' was not found. Verify the export id and try again.");
        return;
    }

    if (exportStatusResponse.Error != null || exportStatusResponse.Result.State == "Error")
    {
        Console.WriteLine(exportStatusResponse.Error);
        return;
    }

    if (exportStatusResponse.Result.State == "InProgress")
    {
        Console.WriteLine($"Export '{exportId}' is still in progress. Checking status again in 5 seconds.");
        Thread.Sleep(5 * 1000);
    }
}
while (exportStatusResponse.Result.State != "Complete");

var indexUrl = exportStatusResponse.Result.IndexUrl;
Console.WriteLine($"Downloading export index file from '{indexUrl}'.");

bool header = false;

if (combineFiles)
{
    await SaveOutputCombined(indexUrl, outputPath);
}
else
{
    await SaveOutputSeparateFiles(indexUrl, outputPath);
}

// Download all segment export files and combine into a single file
async Task SaveOutputCombined(string indexUrl, string outputPath)
{
    using (var client = new HttpClient())
    using (var writer = new StreamWriter(outputPath))
    {
        int playerCount = 0;
        int fileNum = 0;
        var indexData = await client.GetStringAsync(indexUrl);

        var urls = indexData.Split("\n");
        Console.WriteLine($"Downloading segment data from {urls.Length} file(s) and saving to {outputPath}");

        foreach (var url in urls)
        {
            fileNum++;
            int lineNum = 0;

            using (var segmentDataPart = await client.GetStreamAsync(url))
            using (var reader = new StreamReader(segmentDataPart))
            {
                string? line = null;
                do
                {
                    line = reader.ReadLine();
                    if (line != null)
                    {
                        lineNum++;

                        if ((lineNum == 1 && !header))
                        {
                            writer.WriteLine(line);
                            header = true;
                        }
                        else if (lineNum > 1)
                        {
                            writer.WriteLine(line);
                            playerCount++;
                        }
                    }
                }
                while (line != null);
            }
        }
        Console.WriteLine($"Segment export complete. Saved {playerCount} player profiles from segment {segmentId} to {outputPath}.");
    }
}

// Download all segment export files and save separately
async Task SaveOutputSeparateFiles(string indexUrl, string outputPath)
{
    using (var client = new HttpClient())
    {
        var indexData = await client.GetStringAsync(indexUrl);

        var urls = indexData.Split("\n");
        Console.WriteLine($"Downloading segment data from {urls.Length} file(s) and saving to {outputPath}.");

        int fileNum = 0;
        foreach (var url in urls)
        {
            fileNum++;
            string fileName = $"{segmentId}_{fileNum}.tsv";

            using (var segmentDataPart = await client.GetStreamAsync(url))
            using (var reader = new StreamReader(segmentDataPart))
            using (var writer = new StreamWriter(Path.Combine(outputPath, fileName)))
            {
                writer.Write(reader.ReadToEnd());
            }
        }
    }

    Console.WriteLine("Segment export complete.");
}