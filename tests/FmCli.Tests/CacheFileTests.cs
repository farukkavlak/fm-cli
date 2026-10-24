using FmCli;
using Xunit;

namespace FmCli.Tests;

public class CacheFileTests
{
	[Fact]
	public void CacheFilesAreAbsolute()
	{
		// The folder is learned from the game on first use; when it cannot be, it is the shared one.
		// An empty one used to make the path relative and a caller's CreateDirectory throw.
		var path = Program.CacheFile("probe.json");
		Assert.True(Path.IsPathRooted(path), path);
		Assert.Equal("probe.json", Path.GetFileName(path));
	}
}
