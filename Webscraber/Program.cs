using HtmlAgilityPack;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Drawing;

class Program
{
    private const string url = "https://web.ais.dk/aisdata/";
    private const string logFilePath = "downloaded_dates.log";

    static async Task Main()
    {
        Console.CursorVisible = false;

        // Scrape the webpage
        var newestFileLink = await GetNewestFileLink();

        if (newestFileLink is not null)
        {
            Console.WriteLine($"Newest file found: {newestFileLink}");

            // Check if the date is already logged
            string downloadDate = newestFileLink.Item2;
            if (!IsDateDownloaded(downloadDate))
            {
                Console.WriteLine("File has not been downloaded before");

                // Download the zip file
                if (!await DownloadFile(newestFileLink.Item1))
                {
                    Console.WriteLine("No file was downloaded, either due to error or no file was found...");
                    Console.WriteLine("Press a button to close program...");
                    Console.ReadKey();
                    return;
                }

                // Log the date to prevent re-download
                LogDownloadedDate(downloadDate);
            }
            else
            {
                Console.WriteLine("File has already been downloaded previously");
            }
        }
        else
        {
            Console.WriteLine("No new files found");
        }
        Console.WriteLine("Press a button to close...");
        Console.ReadKey();
    }

    static async Task<Tuple<string, string>?> GetNewestFileLink()
    {
        Console.WriteLine("Getting the newest file link...");

        HttpClient client = new();
        var response = await client.GetStringAsync(url);

        // Load the HTML content
        HtmlDocument document = new();
        document.LoadHtml(response);

        // Select all rows from the table
        var rows = document.DocumentNode.SelectNodes("//tr");

        List<Tuple<string, string, DateTime>> files = [];

        foreach (var row in rows)
        {
            // The second column has the link and the third column has the timestamp
            var linkNode = row.SelectSingleNode("./td[2]/a");
            var dateNode = row.SelectSingleNode("./td[3]");

            if (linkNode is not null && dateNode is not null)
            {
                string link = linkNode.Attributes["href"].Value;
                string dateText = dateNode.InnerText.Trim();

                if (DateTime.TryParse(dateText, out DateTime fileDate))
                {
                    fileDate = fileDate.Date;
                    files.Add(Tuple.Create(link, fileDate.ToString("yyyy-MM-dd"), fileDate));
                }
            }
        }

        // Sort by the date to get the newest file
        var newestFile = files.OrderByDescending(f => f.Item3).FirstOrDefault();

        if (newestFile is not null)
        {
            string downloadUrl = $"{url}{newestFile.Item1}";
            return Tuple.Create(downloadUrl, newestFile.Item2);
        }

        return null;
    }

    static bool IsDateDownloaded(string downloadDate)
    {
        if (!File.Exists(logFilePath))
        {
            return false; // If the log file doesn't exist, no dates have been downloaded yet.
        }

        var downloadedDates = File.ReadAllLines(logFilePath);
        return downloadedDates.Contains(downloadDate);
    }

    static void LogDownloadedDate(string downloadDate)
    {
        Console.WriteLine("Loggin download...");
        File.AppendAllText(logFilePath, downloadDate + Environment.NewLine);
    }

    static async Task<bool> DownloadFile(string fileUrl)
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        

        using (var response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();  // Ensure the request is successful

            var fileName = Path.GetFileName(new Uri(fileUrl).AbsolutePath);

            // Get the total size of the file from the response headers
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            if (totalBytes <= 0)
            {
                Console.WriteLine("No bytes available to download...");
                return false;
            }

            try
            {
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    Console.WriteLine($"Downloading {fileName}...");

                    // Download the file in chunks
                    var buffer = new byte[8192];  // 8 KB buffer
                    int totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        totalBytesRead += bytesRead;

                        if (totalBytes != -1)
                        {
                            PrintLoadingBar(totalBytes, totalBytesRead);
                        }
                    }
                    Console.WriteLine($"\nFile downloaded successfully as {fileName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while trying to download the file: " + ex.ToString());
            }

        }
        
        return true;
    }

    private static void PrintLoadingBar(long totalBytes, int totalBytesRead)
    {
        const int progressBarWidth = 50;
        Console.CursorLeft = 0;
        int progress = (int)((double)totalBytesRead / totalBytes * progressBarWidth);
        Console.Write("[");
        Console.Write(new string('#', progress));  // Completed part
        Console.Write(new string('-', progressBarWidth - progress));  // Remaining part
        Console.Write($"] {totalBytesRead / 1024 / 1024}/{totalBytes / 1024 / 1024} MB");
    }
}