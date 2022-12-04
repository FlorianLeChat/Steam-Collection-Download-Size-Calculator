using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCollectionDownloadSizeCalculator;

internal partial class Program
{
	private static bool shouldSave;
	private static List<string> retrievedItems = new();
	private static readonly HttpClient httpClient = new();
	private static readonly TextWriter textMirror = new StreamWriter("output.txt");

	/// <summary>
	/// A regex which matches the numbers "0" to "9" atomically at least once.
	/// </summary>
	[GeneratedRegex("[0-9]+")]
	private static partial Regex FindNumbers();

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

		var userInput = Console.ReadLine()?.Trim();

		ConsoleLog("");

		if (string.IsNullOrWhiteSpace(userInput))
		{
			ConsoleLog("Assessment error. Please enter an identifier.");
			goto retry;
		}

		// We check if the input contains valid identifiers.
		var collectionIds = FindNumbers().Matches(userInput);

		if (collectionIds.Count == 0)
		{
			ConsoleLog("Assessment error. Please enter a valid identifier.");
			goto retry;
		}

		// You are asked if the console output should be saved in a text file.
		ConsoleKey userResponse;

		do
		{
			Console.Write("Do you want to save the console output in a text file? [y/n] ");

			userResponse = Console.ReadKey(false).Key;

			if (userResponse != ConsoleKey.Enter)
				ConsoleLog();
		} while (userResponse != ConsoleKey.Y && userResponse != ConsoleKey.N);

		shouldSave = userResponse == ConsoleKey.Y;

		if (shouldSave)
			ConsoleLog("The console output will be saved into the file \"output.txt\" in the application folder.");

		ConsoleLog();

		// We iterate through all the results.
		foreach (var collectionId in collectionIds)
		{
			// We retrieve all the identifiers through the Steam API.
			var objectId = collectionId?.ToString();

			if (objectId is not null)
			{
				await RequestSteamAPI(objectId);

				retrievedItems = retrievedItems.Distinct().ToList();

				if (retrievedItems.Count == 0)
					ConsoleLog($"The object \"{objectId}\" doesn't contain any element, move to the next one.");

				// Then we calculate the size of the identifiers for this object.
				await CalculateSize();

				retrievedItems.Clear();
			}
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
	private static async Task RequestSteamAPI(string objectId)
	{
		try
		{
			// We prepare the query.
			var parameters = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{"collectioncount", "1"},
				{"publishedfileids[0]", objectId}
			});

			var request = await httpClient.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", parameters);

			if (request.IsSuccessStatusCode && request.Content != null)
			{
				// Then we iterate through the whole JSON file to retrieve the identifiers.
				var document = await JsonDocument.ParseAsync(await request.Content.ReadAsStreamAsync());
				var response = document.RootElement.GetProperty("response").GetProperty("collectiondetails")[0];

				if (response.TryGetProperty("children", out var itemsId))
				{
					ConsoleLog($"The Steam API reports that the object is a Workshop collection containing {itemsId.GetArrayLength()} items.");
					ConsoleLog("Beginning of calculation...");

					foreach (var itemId in itemsId.EnumerateArray())
					{
						if (itemId.TryGetProperty("publishedfileid", out var fileId))
						{
							retrievedItems.Add(fileId.ToString());
						}
					}
				}
				else
				{
					ConsoleLog("The Steam API reports that the object is a simple addon (in some cases, the identifier you entered may be invalid).");
					retrievedItems.Add(objectId);
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
				{"itemcount", retrievedItems.Count.ToString()},
			};

			foreach (var itemId in retrievedItems)
			{
				parameters.Add($"publishedfileids[{index}]", itemId);
				index++;
			}

			// We perform the query and get the result in JSON format.
			var request = await httpClient.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", new FormUrlEncodedContent(parameters));

			if (request.IsSuccessStatusCode && request.Content != null)
			{
				var document = await JsonDocument.ParseAsync(await request.Content.ReadAsStreamAsync());
				var response = document.RootElement.GetProperty("response");

				// Some of the objects previously filled in may not exist so we check that.
				if (response.TryGetProperty("publishedfiledetails", out var fileInfo))
				{
					var totalSize = 0L;
					var currentItem = 1;

					foreach (var itemInfo in fileInfo.EnumerateArray())
					{
						var consoleOutput = $"({currentItem}/{index}) {itemInfo.GetProperty("publishedfileid"),-10} :";

						if (itemInfo.TryGetProperty("title", out var title))
						{
							var itemSize = long.Parse(itemInfo.GetProperty("file_size").ToString());

							ConsoleLog($"{consoleOutput} {title} [{BytesToString(itemSize)}]");

							totalSize += itemSize;
						}
						else
						{
							ConsoleLog($"{consoleOutput} ERROR -> OBJECT IS HIDDEN OR UNAVAILABLE");
						}

						currentItem++;
					}

					ConsoleLog($"Total size: {BytesToString(totalSize)}.");
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