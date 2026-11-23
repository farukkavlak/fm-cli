using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>The panel the planner's Advice list opens for its evaluation, by its asset.</summary>
	private const string EvaluationPanel = "c9d27aaf8333c714b837c4046421fd6b";

	/// <summary>
	/// The planner's Advice list (its AdviceOptionsSelection control), by the game's own FM.UI.AdviceOption
	/// numbers: the assistant's evaluation of the tactic (0), his eleven (1), the opposition report (3).
	/// The upcoming match (2) and the Data Hub (4) only open pages no command reads yet.
	/// </summary>
	private static async Task<int> TacticAdvice(string? what, bool dry)
	{
		var option = Fold(what ?? "") switch
		{
			"" or "evaluation" or "degerlendirme" => 0,
			"xi" or "eleven" or "kadro" => 1,
			"opposition" or "rakip" => 3,
			_ => -1,
		};
		if (option < 0)
		{
			Console.Error.WriteLine(L("usage: tactic advice [evaluation | xi | opposition]"));
			return 64;
		}
		if (Field(await Fm.GetJson("state"), "DuringMatch") == "True")
		{
			Console.Error.WriteLine(L("The planner's advice is not there during a match; the assistant's during a match: dugout"));
			return 1;
		}
		if (await Planner() is null)
		{
			Console.Error.WriteLine(L("The team has no tactic. To create one: tactic new"));
			return 1;
		}
		JsonElement control = default;
		await Until(async () =>
		{
			control = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Events").Contains("AdviceOptionsSelection", StringComparison.Ordinal) && Field(b, "Visible") == "True");
			if (control.ValueKind != JsonValueKind.Object) return false;
			// The list is drawn before its rows are filled; each row holds its AdviceOption number.
			var pointer = Field(control, "Pointer");
			var list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Pointer") == pointer);
			return list.ValueKind == JsonValueKind.Object && list.TryGetProperty("Choices", out var rows)
				&& rows.EnumerateArray().Any(r => r.GetString() == option.ToString());
		}, seconds: 8, every: 0.25);
		if (control.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The planner offers no advice list now."));
			return 1;
		}
		if (dry)
		{
			Console.WriteLine(L($"(dry run) the assistant's advice: {option}"));
			return 0;
		}
		var said = await Fm.Get("select", ("ptr", Field(control, "Pointer")), ("choice", option.ToString()), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		switch (option)
		{
			case 1:
			{
				// His eleven is a conversation: what he offers is answered with talk.
				if (!await Until(async () => (await TalkOptions()).Count > 0, seconds: 8, every: 0.25))
				{
					Console.Error.WriteLine(L("The assistant did not answer."));
					return 1;
				}
				return await TalkTo(false, ["talk"]);
			}
			case 3:
				return await Opponent(false);
			default:
			{
				List<string> lines = [];
				var settled = await Until(async () =>
				{
					var read = (await Fm.GetJson("critiques")).EnumerateArray().Select(l => l.GetString() ?? "").ToList();
					var same = read.Count > 0 && read.SequenceEqual(lines);
					lines = read;
					return same;
				}, seconds: 8, every: 0.25);
				if (settled) foreach (var line in lines) Console.WriteLine($"  {line}");
				else Console.Error.WriteLine(L("The assistant's evaluation could not be read."));
				var close = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(b => Field(b, "Page") == EvaluationPanel && Field(b, "Events").StartsWith("PanelHistoryClose", StringComparison.Ordinal));
				if (close.ValueKind == JsonValueKind.Object) await Fm.Get("press", ("ptr", Field(close, "Pointer")));
				return settled ? 0 : 1;
			}
		}
	}
}
