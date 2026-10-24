using System.Text.Json;

namespace FmCli;

/// <summary>
/// The installation, checked without a game or a save: the Steam copy, BepInEx under it, the
/// shadow bundle the launcher starts, the plugin, the command itself and the save folder. Each
/// failing step says what to do about it, so a new machine can be brought up without guessing
/// (BAGIMLILIKLAR H).
/// </summary>
internal static partial class Program
{
	private static int InstallDoctor(bool json)
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var game = Environment.GetEnvironmentVariable("FM_HOME")
			?? Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Football Manager 26");
		var bundle = Environment.GetEnvironmentVariable("FM_APP") ?? Path.Combine(home, "fm26_bep/fm.app");
		var saves = Path.Combine(home, "Library/Application Support/Sports Interactive/Football Manager 26/games");

		var checks = new List<(string Name, bool Ok, string Detail, string Fix)>
		{
			("Oyun", Directory.Exists(game) && File.Exists(Path.Combine(game, "fm.app/Contents/MacOS/Football Manager 26")),
				game, L("Steam'den Football Manager 26'yı kur; başka yerdeyse FM_HOME ile göster.")),
			("BepInEx çekirdeği", File.Exists(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")),
				Path.Combine(game, "BepInEx"), L("BepInEx 6 IL2CPP arm64 paketini oyun klasörüne aç (FINDINGS §3).")),
			("BepInEx başlatıcısı", File.Exists(Path.Combine(game, "run_bepinex_arm64.sh")),
				Path.Combine(game, "run_bepinex_arm64.sh"), L("BepInEx'in macOS başlatıcısını oyun klasörüne koy.")),
			("Gölge paket", Directory.Exists(bundle) && File.Exists(Path.Combine(bundle, "Contents/Info.plist")),
				bundle, L("Steam kopyasından ~/fm26_bep/fm.app oluştur (Info.plist yaması; README).")),
			("Eklenti", File.Exists(Path.Combine(game, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll")),
				Path.Combine(game, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll"), L("Eklentiyi derleyip BepInEx/plugins/FmCliProbe/ altına kopyala.")),
			("fm komutu", OnPath(home),
				Environment.ProcessPath ?? "fm", L("Derlenmiş fm'yi ~/.local/bin ya da /usr/local/bin'e bağla.")),
			("Kayıt klasörü", Directory.Exists(saves), saves,
				L("Oyunu bir kez açıp kayıt oluştur; klasör kendiliğinden gelir.")),
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(checks.Select(c => new { c.Name, Ok = c.Ok, c.Detail, Fix = c.Ok ? "" : c.Fix })));
			return checks.All(c => c.Ok) ? 0 : 1;
		}

		Console.WriteLine(L("KURULUM DENETİMİ"));
		foreach (var (name, ok, detail, fix) in checks)
		{
			Console.WriteLine($"  {(ok ? "✓" : "✗")} {name,-20} {detail}");
			if (!ok) Console.WriteLine($"      → {fix}");
		}
		var missing = checks.Count(c => !c.Ok);
		Console.WriteLine(missing == 0
			? L("\nKurulum tam: `fm start` ile oyunu açabilirsin.")
			: L($"\n{missing} adım eksik. Yukarıdaki → satırlarını izle; ayrıntı: README."));
		return missing == 0 ? 0 : 1;
	}

	private static bool OnPath(string home)
	{
		var local = Path.Combine(home, ".local/bin/fm");
		return File.Exists(local) || File.Exists("/usr/local/bin/fm") || File.Exists("/opt/homebrew/bin/fm");
	}
}
