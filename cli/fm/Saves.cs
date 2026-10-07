using System.Globalization;

namespace FmCli;

/// <summary>
/// The career saves on disk, newest first: the game keeps the main file, its own rolling backups
/// ("(v02)") and an overwrite backup beside it (FINDINGS §8.35).
/// </summary>
internal static partial class Program
{
	private static readonly string SavesFolder = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		"Library", "Application Support", "Sports Interactive", "Football Manager 26", "games");

	private static int Saves()
	{
		if (!Directory.Exists(SavesFolder))
		{
			Console.Error.WriteLine($"Kayıt klasörü yok: {SavesFolder}");
			return 1;
		}
		var files = new DirectoryInfo(SavesFolder).GetFiles("*.fm").OrderByDescending(f => f.LastWriteTime).ToList();
		if (files.Count == 0)
		{
			Console.WriteLine("Kayıt yok.");
			return 0;
		}
		var tr = CultureInfo.InvariantCulture;
		Table.Write(["KAYIT", "SON YAZILMA", "BOYUT"], files.Select(f => new[]
		{
			Path.GetFileNameWithoutExtension(f.Name),
			f.LastWriteTime.ToString("d MMM yyyy HH:mm", tr),
			$"{f.Length / 1024.0 / 1024.0:0.0} MB",
		}).ToList());
		Console.WriteLine($"\n{SavesFolder}");
		return 0;
	}
}
