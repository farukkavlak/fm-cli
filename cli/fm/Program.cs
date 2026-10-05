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
				"squad" => await Squad(json, args),
				"inbox" => await Inbox(json),
				"next" => await Next(),
				"status" => await Status(),
				"continue" => await Continue(),
				"start" => await Attach(args.Contains("--debug")),
				"help" or "--help" or "-h" => Help(),
				_ => Unknown(command),
			};
		}
		catch (FmUnavailable)
		{
			Console.Error.WriteLine("FM çalışmıyor. `fm start` ile başlat.");
			return 69; // EX_UNAVAILABLE
		}
		catch (FmBusy)
		{
			Console.Error.WriteLine("FM yanıt vermedi — meşgul olabilir (yükleme, maç). Tekrar dene.");
			return 75; // EX_TEMPFAIL
		}
		catch (FmError error)
		{
			Console.Error.WriteLine(error.Message);
			return 1;
		}
	}

	/// <summary>`start` typed inside a session, or with the game already up.</summary>
	private static async Task<int> Attach(bool debug) =>
		await Launcher.Start(Fm, debug) ? 0 : 69;

	private static async Task<int> Continue() => await Launcher.LoadSave(Fm) ? 0 : 69;

	private static async Task<int> Squad(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		if (args.Contains("--fitness")) return await SquadFitness(json);
		if (args.Contains("--morale")) return await SquadMorale(json);

		var squad = await Bound("squad", "SquadScreen");

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
	}

	private static async Task<int> SquadFitness(bool json)
	{
		var squad = await SquadView("Selection Info", "Condition");
		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		// Most tired first: that is the question the column gets looked at for.
		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "Condition") != "")
			.OrderBy(p => Number(p, "Condition"))
			.ThenBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "PositionCombinedString"),
				Percent(p, "Condition"),
				Percent(p, "Sharpness"),
				Field(p, "StatusTitleShort"),
			})
			.ToList();

		Table.Write(new[] { "OYUNCU", "POZİSYON", "KONDİSYON", "MAÇ FORMU", "DURUM" }, rows);
		return 0;
	}

	private static async Task<int> SquadMorale(bool json)
	{
		var squad = await SquadView("Dynamics", "MoraleString");
		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		var rows = squad.EnumerateArray()
			.Where(p => Field(p, "MoraleString") != "")
			.OrderBy(p => Number(p, "Morale"))
			.ThenBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase)
			.Select(p => new[]
			{
				Field(p, "Name"),
				Field(p, "MoraleString"),
				Field(p, "HierarchyInfluenceCategoryString"),
				Field(p, "SocialGroupString").Trim('(', ')', ' '),
			})
			.ToList();

		Table.Write(new[] { "OYUNCU", "MORAL", "HİYERARŞİ", "GRUP" }, rows);
		return 0;
	}

	/// <summary>
	/// The first-team table under one of its views, which is where a column like condition
	/// lives: the table binds only the columns of the view it is showing.
	///
	/// Choosing the view is tried first, since the table is often already up. If it is not, the
	/// squad screen is opened and its First Team tab pressed, the way a user would get there.
	/// </summary>
	private static async Task<JsonElement> SquadView(string view, string marker)
	{
		var squad = await Fm.GetJson("squad");
		if (Carries(squad, marker)) return squad;

		if (!await Choose(view))
		{
			await Bound("squad", "SquadScreen");

			if (!await Until(async () => Sent(await Fm.Get("press", ("text", "First Team"), ("name", "SIButton")))))
				throw new FmError("Kadro ekranında 'First Team' sekmesi bulunamadı.");

			if (!await Until(() => Choose(view)))
				throw new FmError($"Kadro tablosunda '{view}' görünümü bulunamadı.");
		}

		await Until(async () => Carries(squad = await Fm.GetJson("squad"), marker));
		if (!Carries(squad, marker))
			throw new FmError($"'{view}' görünümü seçildi ama '{marker}' verisi gelmedi.");

		return squad;

		async Task<bool> Choose(string choice) =>
			Sent(await Fm.Get("select", ("name", "ViewDropdown"), ("choice", choice)));

		static bool Sent(string answer) => !answer.StartsWith("error", StringComparison.Ordinal);
	}

	private static bool Carries(JsonElement rows, string field) =>
		rows.EnumerateArray().Any(row => row.TryGetProperty(field, out _));

	/// <summary>Polls until a condition holds, since how long the game takes is not ours to know.</summary>
	private static async Task<bool> Until(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			if (await condition()) return true;
			await Task.Delay(TimeSpan.FromSeconds(1.5));
		}
		return false;
	}

	/// <summary>An injured player's condition comes back as -1, which is not a percentage.</summary>
	private static string Percent(JsonElement element, string name) =>
		Number(element, name) is >= 0 and <= 100 and var value ? $"{value}%" : "-";

	private static int Number(JsonElement element, string name) =>
		int.TryParse(Field(element, name), out var number) ? number : int.MaxValue;

	/// <summary>
	/// Reads a shaped endpoint, opening the screen that binds it if nothing is there yet.
	///
	/// Only one screen's data is bound at a time: reading the squad unbinds the inbox and the
	/// other way round, so every command has to be able to bring its own screen back.
	/// </summary>
	private static async Task<JsonElement> Bound(string route, string panel)
	{
		var data = await Fm.GetJson(route);
		if (data.GetArrayLength() > 0) return data;

		await Fm.Get("send",
			("event", "OpenSubtabEvent"),
			("SubtabPanelID", panel),
			("MainTabPanelID", panel),
			("ObjectUniqueID", "0"),
			("SecondaryTab", "false"));

		// Binding lands a few seconds later, and how many depends on what the game is doing, so
		// this waits for the data rather than for a fixed delay.
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
		while (DateTime.UtcNow < deadline)
		{
			await Task.Delay(TimeSpan.FromSeconds(2));

			data = await Fm.GetJson(route);
			if (data.GetArrayLength() > 0) return data;
		}

		return data;
	}

	private static async Task<int> Inbox(bool json)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		var inbox = await Bound("inbox", "PortalScreen");
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
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}

		// Continue is refused outright while a message needs answering, and the game says
		// nothing about it, so the blocking messages are reported instead of a silent no-op.
		var inbox = await Bound("inbox", "PortalScreen");
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
		Console.WriteLine(inGame
			? "FM açık, kayıt yüklü."
			: "FM açık, kayıt yüklü değil — `continue` ile yükle.");
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
			    --debug       pencereyi göster (normalde Dock'ta ve ekranda görünmez)
			  fm              oturuma gir (oyun açıkken)

			  continue        açık oyunda son kaydı yükle
			  squad           kadroyu bas
			    --fitness     kondisyon ve maç formu, en yorgun önce
			    --morale      moral, hiyerarşi ve sosyal grup
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
		Console.Error.WriteLine($"bilinmeyen komut: {command} — komutlar için `help`");
		return 64; // EX_USAGE
	}
}
