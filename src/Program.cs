using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SteamCollectionDownloadSizeCalculator
{
	internal class Program
	{
		private static bool shouldSave;
		private static List<string> retrievedIDs = new();
		private static readonly HttpClient client = new();
		private static readonly List<string> units = new List<string>() { "Bytes", "KB", "MB", "GB", "TB" };
		private static readonly TextWriter textMirror = new StreamWriter("output.txt");

		/// <summary>
		/// A simple method to display messages in the console and save them in a text file.
		/// </summary>
		private static void ConsoleLog(string text = "", bool noNewline = false)
		{
			text = text.Replace("\n", "");

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
		private static async Task Main()
		{
			// We ask to enter one or more identifiers.
			ConsoleLog("-----------------------------------------");
			ConsoleLog("Steam Collection Download Size Calculator");
			ConsoleLog("-----------------------------------------");

			ConsoleLog("");

			ConsoleLog("Please provide a Workshop object identifier (you can also put several in a row by putting \";\" between each).");
			ConsoleLog("Example: \"https://steamcommunity.com/sharedfiles/filedetails/?id=1448345830\" or \"1448345830;947461782\".");

		retry:

			ConsoleLog("");

			ConsoleLog("=> ", true);

			var input = Console.ReadLine().Trim();
			ConsoleLog("");

			if (string.IsNullOrWhiteSpace(input))
			{
				ConsoleLog("Assessment error. Please enter an identifier.");
				goto retry;
			}

			// We check if the input contains valid identifiers.
			var matches = Regex.Matches(input, "[0-9]+");

			if (matches.Count == 0)
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

			// We iterate through all the results.
			foreach (var match in matches)
			{
				// We retrieve all the identifiers through the Steam API.
				var identifier = match.ToString();

				await RequestSteamAPI(identifier);

				retrievedIDs = retrievedIDs.Distinct().ToList();

				if (retrievedIDs.Count == 0)
					ConsoleLog($"The object \"{identifier}\" doesn't contain any element, move to the next one.");

				// Then we calculate the size of the identifiers for this object.
				await CalculateSize();

				retrievedIDs.Clear();
			}

			ConsoleLog("Program terminated. Thanks for using it :D");

			if (shouldSave)
			{
				ConsoleLog("Note: The output file will be automatically overwritten the next time you launch the application.");

				textMirror.Flush();
				textMirror.Close();
			}

			_ = Console.ReadLine();
		}

		/// <summary>
		/// Retrieves all the identifiers of a collection using the Steam "ISteamRemoteStorage" API.
		/// </summary>
		private static async Task RequestSteamAPI(string requestedID)
		{
			try
			{
				// We prepare the query.
				var parameters = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					{"collectioncount", "1"},
					{"publishedfileids[0]", requestedID}
				});

				var request = await client.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", parameters);

				if (request.IsSuccessStatusCode && request.Content != null)
				{
					// Then we iterate through the whole JSON file to retrieve the identifiers.
					var document = await JsonDocument.ParseAsync(await request.Content.ReadAsStreamAsync());
					var details = document.RootElement.GetProperty("response").GetProperty("collectiondetails")[0];

					if (details.TryGetProperty("children", out var items))
					{
						ConsoleLog($"The Steam API reports that the object is a Workshop collection containing {items.GetArrayLength()} items.");
						ConsoleLog("Beginning of calculation...");

						foreach (var item in items.EnumerateArray())
						{
							if (item.TryGetProperty("publishedfileid", out var identifier))
							{
								retrievedIDs.Add(identifier.ToString());
							}
						}
					}
					else
					{
						ConsoleLog("The Steam API reports that the object is a simple addon (in some cases, the identifier you entered may be invalid).");
						retrievedIDs.Add(requestedID);
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
		/// Transforms bytes into a human readable string.
		/// Source: https://stackoverflow.com/a/11124118
		/// </summary>
		private static string BytesToString(long size)
		{
			string suffix;
			double readable;

			if (size >= 0x1000000000000000) // Exabyte
			{
				suffix = "EB";
				readable = size >> 50;
			}
			else if (size >= 0x4000000000000) // Petabyte
			{
				suffix = "PB";
				readable = size >> 40;
			}
			else if (size >= 0x10000000000) // Terabyte
			{
				suffix = "TB";
				readable = size >> 30;
			}
			else if (size >= 0x40000000) // Gigabyte
			{
				suffix = "GB";
				readable = size >> 20;
			}
			else if (size >= 0x100000) // Megabyte
			{
				suffix = "MB";
				readable = size >> 10;
			}
			else if (size >= 0x400) // Kilobyte
			{
				suffix = "KB";
				readable = size;
			}
			else
			{
				return size.ToString("0 B"); // Byte
			}

			readable /= 1024;

			return readable.ToString("0.## ") + suffix;
		}

		/// <summary>
		/// Calculates the total size of all the elements retrieved previously.
		/// </summary>
		private static async Task CalculateSize()
		{
			try
			{
				// We fill in all the identifiers to make a single request.
				var index = 0;
				var parameters = new Dictionary<string, string>
				{
					{"itemcount", retrievedIDs.Count.ToString()},
				};

				foreach (var identifier in retrievedIDs)
				{
					parameters.Add($"publishedfileids[{index}]", identifier);
					index++;
				}

				// We perform the query and get the result in JSON format.
				var request = await client.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", new FormUrlEncodedContent(parameters));

				if (request.IsSuccessStatusCode && request.Content != null)
				{
					var document = await JsonDocument.ParseAsync(await request.Content.ReadAsStreamAsync());
					var details = document.RootElement.GetProperty("response");

					// Some of the objects previously filled in may not exist so we check that.
					if (details.TryGetProperty("publishedfiledetails", out var items))
					{
						var count = 1;
						var total = 0L;

						foreach (var item in items.EnumerateArray())
						{
							var message = $"({count}/{index}) {item.GetProperty("publishedfileid"),-10} :";

							if (item.TryGetProperty("title", out var title))
							{
								var size = long.Parse(item.GetProperty("file_size").ToString());

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
						ConsoleLog();
					}
					else
					{
						ConsoleLog("It seems that the object is invalid or simply temporarily unavailable.");
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