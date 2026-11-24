using FmCli.Nl;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// `ai`: smart mode's cloud model. Alone, what is set up; `ai set <url> <model> [--key K]
	/// [--header "Name: value"]` sets it up, `ai test` asks it one sentence, `ai off` forgets it.
	/// The key is the user's and stays in their own file.
	/// </summary>
	private static async Task<int> AiCommand(string[] args)
	{
		var words = Words(args).ToList();
		var verb = words.Count > 0 ? Nl.Text.Fold(words[0]) : "";
		if (verb is "off" or "kapat")
		{
			Cloud.Save(null);
			Console.WriteLine(L("Smart mode off: sentences stay on this computer."));
			return 0;
		}
		if (verb is "set" or "kur")
		{
			if (words.Count < 3)
			{
				Console.Error.WriteLine(L("Give the provider's URL and the model: ai set <url> <model> [--key <key>] [--header \"Name: value\"]"));
				return 64;
			}
			var key = Option(args, "--key") ?? Environment.GetEnvironmentVariable("FM_AI_KEY") ?? "";
			if (key.Length == 0 && Ui.Interactive)
			{
				Console.Write(L("API key (Enter: none): "));
				key = Console.ReadLine()?.Trim() ?? "";
			}
			Dictionary<string, string>? headers = null;
			if (Option(args, "--header") is { } h && h.Split(':', 2) is [var name, var value])
				headers = new() { [name.Trim()] = value.Trim() };
			Cloud.Save(new Cloud.Settings(words[1], key, words[2], headers));
			Console.WriteLine(L($"Smart mode on: {words[2]}. Sentences the router is not sure of are sent there. Try it: ai test"));
			return 0;
		}

		if (Cloud.Current() is not { } s)
		{
			Console.WriteLine(L("Smart mode is off: sentences stay on this computer."));
			Console.WriteLine(L("To ask a cloud model when the router is not sure: ai set <url> <model> --key <key>  (any OpenAI-style provider)"));
			return 0;
		}
		if (verb is "test" or "dene")
		{
			var said = await Cloud.Propose(s, "show me the squad", [("fm_squad", "The squad", "{}"), ("fm_table", "The league table", "{}")]);
			if (said.Tool != "fm_squad")
			{
				Console.Error.WriteLine(L($"{s.Model} did not answer as asked ({said.Seconds:0.0} s). Check the URL, model and key: ai"));
				return 1;
			}
			Console.WriteLine(L($"{s.Model} answered right in {said.Seconds:0.0} s."));
			return 0;
		}
		Console.WriteLine(L($"Smart mode on: {s.Model}"));
		Console.WriteLine($"  URL  {s.Url}");
		Console.WriteLine($"  {L("key")}  {(s.Key.Length > 0 ? L("set") : L("none"))}");
		foreach (var (name, _) in s.Headers ?? []) Console.WriteLine($"  {name}");
		Console.WriteLine(Dim(L("Sentences the router is not sure of are sent there. ai test · ai off")));
		return 0;
	}
}
