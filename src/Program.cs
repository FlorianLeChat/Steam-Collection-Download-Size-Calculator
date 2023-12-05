using System.Text.Json;
using System.Text.RegularExpressions;

namespace SteamCollectionDownloadSizeCalculator;

public partial class Calculator
{
	private static bool shouldSave;
	private static string order = "none";
	private static TextWriter? textMirror = null;
	private static List<string> retrievedItems = new();
	private static readonly HttpClient httpClient = new();

	/// <summary>
	/// A regular expression which atomically matches numbers "0" to "9" at least once.
	/// </summary>
	[GeneratedRegex("[0-9]+")]
	private static partial Regex FindNumbers();

	/// <summary>
	/// A simple method for displaying messages in the console and saving them in a text file.
	/// </summary>
	private static void ConsoleLog(string text = "", bool noNewline = false)
	{
		text = text.Replace("\n", "");

		if (noNewline)
			Console.Write(text);
		else
			Console.WriteLine(text);

		if (shouldSave)
			textMirror?.WriteLine(text);
	}

	/// <summary>
	/// Main program function which retrieves identifier and launches size calculation functions.
	/// </summary>
	private static async Task Main()
	{
		// Prompt to enter one or more identifiers.
		programStart:

		ConsoleLog("-----------------------------------------");
		ConsoleLog("Steam Collection Download Size Calculator");
		ConsoleLog("-----------------------------------------");

		ConsoleLog("");

		ConsoleLog("Please provide a Workshop object identifier (you can also put several in a row by putting \";\" between each).");
		ConsoleLog("Example: \"https://steamcommunity.com/sharedfiles/filedetails/?id=1448345830\" or \"1448345830;947461782\".");

		checkIdentifier:

		ConsoleLog("");
		ConsoleLog("=> ", true);

		var identifiers = Console.ReadLine()?.Trim();

		ConsoleLog("");

		if (string.IsNullOrWhiteSpace(identifiers))
		{
			ConsoleLog("Validation error. Please enter an identifier.");
			goto checkIdentifier;
		}

		// Check for valid identifiers.
		var collectionIds = FindNumbers().Matches(identifiers);

		if (collectionIds.Count == 0)
		{
			ConsoleLog("Validation error. Please enter a valid identifier.");
			goto checkIdentifier;
		}

		// Request sort order of results after processing.
		if (order == "none")
		{
			ConsoleLog("Do you want to order objects by size?");
			ConsoleLog("Type \"ASC\" for ascending sorting, \"DESC\" for descending sorting, or leave blank if you don't want any sorting.");

			checkOrder:

			ConsoleLog("");
			ConsoleLog("=> ", true);

			var requestOrder = Console.ReadLine()?.Trim().ToUpper();

			ConsoleLog("");

			switch (requestOrder)
			{
				case "ASC":
					order = "ASC";
					ConsoleLog("Ascending sorting (smallest files first).");
					break;

				case "DESC":
					order = "DESC";
					ConsoleLog("Descending sorting (heaviest files first).");
					break;

				case "":
					ConsoleLog("No sorting (files sorted in order they were added to the collection).");
					break;

				default:
					ConsoleLog("Invalid selection. Choose a valid sort or leave blank to ignore this step.");
					goto checkOrder;
			}

			ConsoleLog("");
		}

		// Prompt if console output should be saved to a text file.
		if (textMirror == null)
		{
			ConsoleKey requestSave;

			do
			{
				Console.Write("Do you want to save console output to a text file? [y/N] ");

				requestSave = Console.ReadKey(false).Key;

				ConsoleLog();

				if (requestSave == ConsoleKey.Enter)
					break;
			} while (requestSave != ConsoleKey.Y && requestSave != ConsoleKey.N);

			shouldSave = requestSave == ConsoleKey.Y;

			if (shouldSave)
			{
				textMirror = new StreamWriter("output.log");
				ConsoleLog("Console output will be saved in a \"output.log\" file in application directory.");
			}
			else
			{
				ConsoleLog("Console output will not be saved for this time.");
			}

			ConsoleLog();
		}

		// Iterate through all results.
		foreach (var collectionId in collectionIds)
		{
			// Retrieve all identifiers using Steam API.
			var objectId = collectionId?.ToString();

			if (objectId is not null)
			{
				await RequestSteamAPI(objectId);

				retrievedItems = retrievedItems.Distinct().ToList();

				if (retrievedItems.Count == 0)
					ConsoleLog($"Object \"{objectId}\" does not contain any elements, move on to next one.");

				// Calculate total object size.
				await CalculateSize();

				retrievedItems.Clear();
			}
		}

		// Saves contents in log file between each run.
		if (shouldSave)
		{
			textMirror?.Flush();
		}

		// Prompts the user to restart the program.
		ConsoleKey requestRetry;

		do
		{
			Console.Write("Do you need to calculate another collection? [Y/n] ");

			requestRetry = Console.ReadKey(false).Key;

			ConsoleLog();

			if (requestRetry == ConsoleKey.Enter)
				break;
		} while (requestRetry != ConsoleKey.Y && requestRetry != ConsoleKey.N);

		if (requestRetry != ConsoleKey.N)
		{
			ConsoleLog("Program is restarting...");
			ConsoleLog();

			goto programStart;
		}
		else
		{
			ConsoleLog("Program terminated. Thanks for using it :D");

			if (shouldSave)
			{
				ConsoleLog("Note: output file will be automatically overwritten next time you launch the application.");

				textMirror?.Flush();
				textMirror?.Close();
			}

			_ = Console.ReadLine();
		}
	}

	/// <summary>
	/// Retrieves all identifiers in a collection using the "ISteamRemoteStorage" Steam API.
	/// </summary>
	private static async Task RequestSteamAPI(string objectId)
	{
		try
		{
			// Prepare the query.
			var parameters = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				{"collectioncount", "1"},
				{"publishedfileids[0]", objectId}
			});

			var request = await httpClient.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", parameters);

			if (request.IsSuccessStatusCode && request.Content != null)
			{
				// Query the entire JSON file to retrieve identifiers.
				var document = await JsonDocument.ParseAsync(await request.Content.ReadAsStreamAsync());
				var response = document.RootElement.GetProperty("response").GetProperty("collectiondetails")[0];

				if (response.TryGetProperty("children", out var itemsId))
				{
					ConsoleLog($"Steam API says that the object is a Workshop collection containing {itemsId.GetArrayLength()} items.");
					ConsoleLog("Starting computation...");
					ConsoleLog();

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
					ConsoleLog("Steam API says that the object is a simple addon (in some cases, the identifier you've provided may be invalid).");
					retrievedItems.Add(objectId);
				}
			}
			else
			{
				ConsoleLog("A network error occurred while requesting Steam servers. Please try again later.");
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
	public static string BytesToString(long size)
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
	/// Calculates total size of all previously retrieved elements.
	/// </summary>
	private static async Task CalculateSize()
	{
		try
		{
			// Fill in all identifiers to make a single request.
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

			// Perform query and retrieve result in JSON format.
			var request = await httpClient.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", new FormUrlEncodedContent(parameters));

			if (request.IsSuccessStatusCode && request.Content != null)
			{
				var document = await JsonDocument.ParseAsync(await request.Content.ReadAsStreamAsync());
				var response = document.RootElement.GetProperty("response");

				// Some objects previously filled in may not exist.
				if (response.TryGetProperty("publishedfiledetails", out var fileInfo))
				{
					var totalSize = 0L;
					var arrayItems = fileInfo.EnumerateArray();
					var currentItem = 1;
					IEnumerable<JsonElement>? sortedItems = null;

					if (order == "ASC")
					{
						// Ascending sorting.
						sortedItems = arrayItems.OrderBy(itemInfo => long.Parse(itemInfo.GetProperty("file_size").ToString()));
					}
					else if (order == "DESC")
					{
						// Descending sorting.
						sortedItems = arrayItems.OrderByDescending(itemInfo => long.Parse(itemInfo.GetProperty("file_size").ToString()));
					}
	
					foreach (var itemInfo in (sortedItems ?? arrayItems))
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
					ConsoleLog("It seems that the object is invalid or just temporarily unavailable.");
				}
			}
			else
			{
				ConsoleLog("A network error occurred while requesting Steam servers. Please try again later.");
			}
		}
		catch (Exception error)
		{
			Console.Error.WriteLine(error.Message);
		}
	}
}