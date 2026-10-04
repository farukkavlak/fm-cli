using System.Text.Json;

namespace FmCli;

internal static class Program
{
	private static readonly Bridge Fm = new();

	public static async Task<int> Main(string[] args)
	{
		// With no command, drop into the session rather than printing help: the game is usually
		// already running and typing "fm" before every command gets old.
		if (args.Length == 0) return await Session.Run(Run);

		if (args[0] == "start")
		{
			if (!await Launcher.Start(Fm, args.Contains("--debug"))) return 69;
			return await Session.Run(Run);
		}

		return await Run(args);
	}

	/// <summary>Runs one command, whether it came from argv or from the session prompt.</summary>
	public static async Task<int> Run(string[] args)
	{
		var command = args.Length > 0 ? args[0] : "help";
		var json = args.Contains("--json");

		try
		{
			return command switch
			{
				"squad" => await Squad(json),
				"inbox" => await Inbox(json),
				"next" => await Next(),
				"status" => await Status(),
				"start" => Started(),
				"help" or "--help" or "-h" => Help(),
				_ => Unknown(command),
			};
		}
		catch (FmUnavailable)
		{
			Console.Error.WriteLine("FM çalışmıyor. `fm start` ile başlat.");
			return 69; // EX_UNAVAILABLE
		}
		catch (FmError error)
		{
			Console.Error.WriteLine(error.Message);
			return 1;
		}
	}

	private static int Started()
	{
		Console.WriteLine("FM zaten açık.");
		return 0;
	}

	private static async Task<int> Squad(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile son kaydı aç.");
			return 69;
		}

		var squad = await Load();
		if (squad.GetArrayLength() == 0)
		{
			// The squad only exists in the binding tree once its screen has bound it, and that
			// takes a frame or two, so the screen is opened and the read retried.
			await Fm.Get("send",
				("event", "OpenSubtabEvent"),
				("SubtabPanelID", "SquadScreen"),
				("MainTabPanelID", "SquadScreen"),
				("ObjectUniqueID", "0"),
				("SecondaryTab", "false"));

			await Task.Delay(TimeSpan.FromSeconds(5));
			squad = await Load();
		}

		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		var rows = squad.EnumerateArray()
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "PositionCombinedString"),
				Field(p, "Age"),
				Field(p, "Team"),
			})
			.OrderBy(r => r[3], StringComparer.OrdinalIgnoreCase)
			.ThenBy(r => r[0], StringComparer.OrdinalIgnoreCase)
			.ToList();

		// The squad screen binds a nation flag rather than its name, so there is no country
		// column to print until a view that carries one is opened.
		Table.Write(new[] { "OYUNCU", "POZİSYON", "YAŞ", "TAKIM" }, rows);
		return 0;

		async Task<JsonElement> Load() => await Fm.GetJson("squad");
	}

	private static async Task<int> Inbox(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile son kaydı aç.");
			return 69;
		}

		var inbox = await Fm.GetJson("inbox");
		if (json)
		{
			Console.WriteLine(inbox.ToString());
			return 0;
		}

		var rows = inbox.EnumerateArray()
			.Select(m => new[]
			{
				Field(m, "NewsItemTimeSent"),
				Field(m, "Sender"),
				Field(m, "NewsTitleString"),
				Flags(m),
			})
			.ToList();

		Table.Write(new[] { "SAAT", "GÖNDEREN", "KONU", "" }, rows);

		var waiting = inbox.EnumerateArray().Count(Blocking);
		if (waiting > 0)
			Console.WriteLine($"\n{waiting} mesaj cevap bekliyor — bunlar `fm next`'i blokluyor.");

		return 0;
	}

	private static async Task<int> Next()
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile son kaydı aç.");
			return 69;
		}

		// Continue is refused outright while a message needs answering, and the game says
		// nothing about it, so the blocking messages are reported instead of a silent no-op.
		var inbox = await Fm.GetJson("inbox");
		var blocking = inbox.EnumerateArray().Where(Blocking).ToList();
		if (blocking.Count > 0)
		{
			Console.Error.WriteLine($"Bloke: {blocking.Count} mesaj cevap bekliyor.");
			foreach (var message in blocking)
				Console.Error.WriteLine($"  {Field(message, "NewsItemTimeSent")}  {Field(message, "NewsTitleString")}");

			return 1;
		}

		Console.WriteLine(await Fm.Get("next"));
		return 0;
	}

	private static async Task<int> Status()
	{
		var inGame = await Fm.InGame();
		Console.WriteLine(inGame ? "FM açık, kayıt yüklü." : "FM açık, kayıt yüklü değil.");
		return inGame ? 0 : 69;
	}

	private static bool Blocking(JsonElement message) =>
		Field(message, "CanRespond") == "True" && Field(message, "NewsHasResponded") != "True";

	private static string Flags(JsonElement message)
	{
		var flags = new List<string>();
		if (Field(message, "NewsItemHasBeenRead") != "True") flags.Add("okunmadı");
		if (Blocking(message)) flags.Add("cevap bekliyor");
		return string.Join(", ", flags);
	}

	private static string Field(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

	private static int Help()
	{
		Console.WriteLine("""
			fm — Football Manager 26'yı terminalden yönet

			  fm start        oyunu başlat, kaydı yükle, oturuma gir
			    --debug       pencereyi gizleme (log'u da söyler)
			  fm              oturuma gir (oyun açıkken)

			  squad           kadroyu bas
			  inbox           gelen kutusu
			  next            zamanı ilerlet
			  status          bağlantı durumu

			  --json          ham veri (script'lenebilir)

			Oturumda komutları `fm` yazmadan verirsin; çıkmak için `quit`.
			""");
		return 0;
	}

	private static int Unknown(string command)
	{
		Console.Error.WriteLine($"bilinmeyen komut: {command}");
		Help();
		return 64; // EX_USAGE
	}
}
