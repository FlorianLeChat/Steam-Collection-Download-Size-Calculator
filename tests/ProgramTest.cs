using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SteamCollectionDownloadSizeCalculator.Tests;

[TestClass]
public class CalculatorTests
{
	[TestMethod]
	public void BytesToStringTest()
	{
		var bytes = Calculator.BytesToString(1);
		var kiloBytes = Calculator.BytesToString(1024);
		var megaBytes = Calculator.BytesToString(1024 * 1024);
		var gigaBytes = Calculator.BytesToString(1024 * 1024 * 1024);

		Assert.AreEqual("1 B", bytes);
		Assert.AreEqual("1 KB", kiloBytes);
		Assert.AreEqual("1 MB", megaBytes);
		Assert.AreEqual("1 GB", gigaBytes);
	}
}