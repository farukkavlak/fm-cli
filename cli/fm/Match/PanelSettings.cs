namespace FmCli;

internal static partial class Program
{
	/// <summary>`fm settings panel`: the live match panel's view, pitch, event count and colours.</summary>
	private static int PanelSettings(List<string> words)
	{
		var config = PanelConfig.Load();
		if (words.Count == 0) return ShowPanel(config);

		for (var at = 0; at < words.Count;)
		{
			var what = words[at++].ToLowerInvariant();
			switch (what)
			{
				case "view":
				{
					if (at >= words.Count || PanelConfig.ParseView(words[at]) is not { } view)
					{
						Console.Error.WriteLine(L("Panel view: full, key or score"));
						return 64;
					}
					config = config with { View = view };
					at++;
					break;
				}
				case "pitch":
				{
					if (at >= words.Count || Flag(words[at]) is not { } pitch)
					{
						Console.Error.WriteLine(L("The pitch must be on or off."));
						return 64;
					}
					config = config with { Pitch = pitch };
					at++;
					break;
				}
				case "events":
				{
					if (at >= words.Count || !int.TryParse(words[at], out var events) || events is < 0 or > 12)
					{
						Console.Error.WriteLine(L("The event count must be 0–12."));
						return 64;
					}
					config = config with { Events = events };
					at++;
					break;
				}
				case "colour" or "color":
				{
					if (at >= words.Count || Flag(words[at]) is not { } colour)
					{
						Console.Error.WriteLine(L("Colour must be on or off."));
						return 64;
					}
					config = config with { Colour = colour };
					at++;
					break;
				}
				case "reset":
					config = PanelConfig.Default;
					break;
				default:
					Console.Error.WriteLine(L($"'{what}' is not a panel setting: view, pitch, events, colour, reset"));
					return 64;
			}
		}

		PanelConfig.Save(config);
		return ShowPanel(config);

		static bool? Flag(string text) => text.ToLowerInvariant() switch
		{
			"on" or "yes" or "true" or "1" => true,
			"off" or "no" or "false" or "0" => false,
			_ => null,
		};
	}

	private static int ShowPanel(PanelConfig config)
	{
		var view = L(config.View switch { "key" => "key moments", "score" => "score only", _ => "full" });
		var on = L("on");
		var off = L("off");
		Console.WriteLine(Lf("Panel: {0} · pitch {1} · {2} events · colour {3}",
			view, config.Pitch ? on : off, config.Events, config.Colour ? on : off));
		Console.WriteLine(Lf("File: {0}", PanelConfig.File));
		return 0;
	}
}
