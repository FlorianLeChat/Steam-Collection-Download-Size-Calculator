using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SteamCollectionDownloadSizeCalculator
{
    class Program
    {
        static string requestedID;
        static bool shouldSave = false;
        static TextWriter textMirror = new StreamWriter("output.txt");
        static List<string> identifiers = new List<string>();
        static readonly HttpClient client = new HttpClient();

        /// <summary>
        /// A simple method to display messages in the console and save them in a text file.
        /// </summary>
        private static void ConsoleLog(string text = "", bool noNewline = false)
        {
            if (noNewline)
                Console.Write(text);
            else
                Console.WriteLine(text);

            if (shouldSave)
                textMirror.WriteLine(text);
        }

        /// <summary>
        /// Main function of the program which retrieves the identifier and launches the functions to calculate the size.
        /// </summary>
        static async Task Main(string[] args)
        {
            // We check the validity of the identifier.
            ConsoleLog("-----------------------------------------");
            ConsoleLog("Steam Collection Download Size Calculator");
            ConsoleLog("-----------------------------------------");

            ConsoleLog("");

            ConsoleLog("Please provide a Workshop object ID (this can also be an addon).");
            ConsoleLog("Example: \"https://steamcommunity.com/sharedfiles/filedetails/?id=1448345830\" or just \"1448345830\".");

            retry:

            ConsoleLog("");

            Console.Write("=> ");

            requestedID = Console.ReadLine().Trim();
            ConsoleLog("");

            if (string.IsNullOrWhiteSpace(requestedID))
            {
                ConsoleLog("Assessment error. Please enter an identifier.");
                goto retry;
            }

            // We check if the identifier is a number.
            var match = Regex.Match(requestedID, "[0-9]+");

            if (match.Success)
            {
                requestedID = match.Value;
            }
            else
            {
                ConsoleLog("Assessment error. Please enter a valid identifier.");
                goto retry;
            }

            // You are asked if the console output should be saved in a text file.
            ConsoleKey response;

            do
            {
                Console.Write("Do you want to save the console output in a text file? [y/n] ");

                response = Console.ReadKey(false).Key;

                if (response != ConsoleKey.Enter)
                    ConsoleLog();

            } while (response != ConsoleKey.Y && response != ConsoleKey.N);

            shouldSave = response == ConsoleKey.Y;

            if (shouldSave)
                ConsoleLog("The console output will be saved into the file \"output.txt\" in the application folder.");

            ConsoleLog();

            // We retrieve all the identifiers of the collection.
            await RequestSteamAPI();

            identifiers = identifiers.Distinct().ToList();

            if (identifiers.Count == 0)
            {
                ConsoleLog("This object doesn't contain any elements");
                goto terminate;
            }

            // Then we iterate to calculate the total size.
            await CalculateSize();

            terminate:

            ConsoleLog("Program terminated. Thanks for using it :D");

            if (shouldSave)
            {
                ConsoleLog("Note: The output file will be automatically overwritten the next time you launch the application.");

                textMirror.Flush();
                textMirror.Close();
            }

            Console.ReadLine();
        }

        /// <summary>
        /// Retrieves all the identifiers of a collection using the Steam "ISteamRemoteStorage" API.
        /// </summary>
        static async Task RequestSteamAPI()
        {
            try
            {
                // We prepare the query.
                var parameters = new Dictionary<string, string>
                {
                    {"collectioncount", "1"},
                    {"publishedfileids[0]", requestedID}
                };

                var content = new FormUrlEncodedContent(parameters);
                var request = await client.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", content);

                if (request.IsSuccessStatusCode && request.Content != null)
                {
                    var jsonText = await request.Content.ReadAsStringAsync();

                    using (var document = JsonDocument.Parse(jsonText))
                    {
                        // Then we iterate through the whole JSON file to retrieve the identifiers.
                        var root = document.RootElement;
                        var details = root.GetProperty("response").GetProperty("collectiondetails")[0];

                        if (details.TryGetProperty("children", out var items))
                        {
                            ConsoleLog($"The Steam API reports that the object is a Workshop collection containing {items.GetArrayLength()} items.");
                            ConsoleLog("Beginning of calculation...");

                            foreach (var item in items.EnumerateArray())
                            {
                                if (item.TryGetProperty("publishedfileid", out var identifier))
                                {
                                    identifiers.Add(identifier.ToString());
                                }
                            }
                        }
                        else
                        {
                            ConsoleLog("The Steam API reports that the object is a simple addon (in some cases, the identifier you entered may be invalid).");
                            identifiers.Add(requestedID);
                        }
                    }
                }
                else
                {
                    ConsoleLog("A network error occurred while requesting the Steam servers. Please try again later.");
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
            }
        }

        /// <summary>
        /// Transforms bytes into human readable string.
        /// https://github.com/Facepunch/garrysmod/blob/master/garrysmod/lua/includes/extensions/string.lua#L257-L268 (Garry's Code ™)
        /// </summary>
        private static string BytesToString(ulong size)
        {
            if (size <= 0)
                return "0 Byte";

            if (size < 1000)
                return size + " Bytes";

            if (size < 1000 * 1000)
                return Math.Round(size / 1000.0, 2) + " KB";

            if (size < 1000 * 1000 * 1000)
                return Math.Round(size / (1000.0 * 1000.0), 2) + " MB";

            return Math.Round(size / (1000.0 * 1000.0 * 1000.0), 2) + " GB";
        }

        /// <summary>
        /// Calculates the total size of all the elements retrieved previously.
        /// </summary>
        static async Task CalculateSize()
        {
            try
            {
                // We fill in all the identifiers to make a single request.
                var index = 0;
                var parameters = new Dictionary<string, string>
                {
                    {"itemcount", identifiers.Count.ToString()},
                };

                foreach (var identifier in identifiers)
                {
                    parameters.Add($"publishedfileids[{index}]", identifier);
                    index++;
                }

                // We perform the query and get the result in JSON format.
                var content = new FormUrlEncodedContent(parameters);
                var request = await client.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", content);

                if (request.IsSuccessStatusCode && request.Content != null)
                {
                    var jsonText = await request.Content.ReadAsStringAsync();

                    using (var document = JsonDocument.Parse(jsonText))
                    {
                        var root = document.RootElement;
                        var details = root.GetProperty("response");

                        // Some of the objects previously filled in may not exist so we check that.
                        if (details.TryGetProperty("publishedfiledetails", out var items))
                        {
                            var count = 1;
                            var total = 0UL;

                            foreach (var item in items.EnumerateArray())
                            {
                                var identifier = item.GetProperty("publishedfileid");
                                var message = $"({count}/{index}) {identifier, -10} :";

                                if (item.TryGetProperty("title", out var title))
                                {
                                    var size = 0UL;

                                    UInt64.TryParse(item.GetProperty("file_size").ToString(), out size);

                                    ConsoleLog($"{message} {title} [{BytesToString(size)}]");

                                    total += size;
                                }
                                else
                                {
                                    ConsoleLog($"{message} ERROR -> OBJECT IS HIDDEN OR UNAVAILABLE");
                                }

                                count++;
                            }

                            ConsoleLog($"Total size: {BytesToString(total)}.");
                        }
                        else
                        {
                            ConsoleLog("It seems that the object is invalid or simply temporarily unavailable.");
                        }
                    }
                }
                else
                {
                    ConsoleLog("A network error occurred while requesting the Steam servers. Please try again later.");
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
            }
        }
    }
}