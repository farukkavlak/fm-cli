using System.Text.Json;

namespace FmCli;

/// <summary>
/// Putting one of our players on the transfer list, or taking him off it. His actions hold
/// "Set Transfer Status" (PersonSetPlayersTransferStatus), which opens the status window of the
/// SetTransferStatus edit session: its list bound to SetTransferStatusTransferStatus holds the
/// game's values (0 not set, 1 transfer listed). The value is chosen, Confirm
/// (EventFmxConfirmChanges) sends it, and his status flags are read back: TransferListed (5).
/// The game asks nothing and it is undone by --off, so it is done at once; --status only reads.
/// </summary>
internal static partial class Program
{
	private const string ListingAction = "PersonSetPlayersTransferStatus", ListingBinds = "status.SetTransferStatusTransferStatus";
	private const int Unlisted = 0, Listed = 1, TransferListedFlag = 5;

	private static async Task<int> Listing(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Ui.Hint(L("usage: listing <player> [--off] [--status]   (put one of our players on the transfer list, --off takes him off it, --status only says where he stands)"), Console.Error);
			return 64;
		}
		var off = args.Contains("--off");
		var wanted = off ? Unlisted : Listed;

		if (await PersonMenu(string.Join(" ", words), "Player") is not { } menu) return 1;
		var name = menu.Name;
		// Where he stands now, from his own status flags: the window's list shows a default until the session fills it.
		async Task<bool?> OnList() => (await SquadFlags()).TryGetValue(name, out var mine) ? mine.Any(f => f.Id == TransferListedFlag) : null;
		var before = await OnList();
		if (before == null)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name} is not in our squad."));
			return 1;
		}
		// --status asks where he stands and changes nothing
		if (args.Contains("--status"))
		{
			await Fm.Get("actions", ("close", "1"));
			if (json) Console.WriteLine(JsonOut.Of(new { Name = name, Listed = before.Value }));
			else Console.WriteLine(before.Value ? L($"{name} is already on the transfer list.") : L($"{name} is not on the transfer list."));
			return 0;
		}
		if (before == !off)
		{
			await Fm.Get("actions", ("close", "1"));
			Console.WriteLine(off ? L($"{name} is not on the transfer list.") : L($"{name} is already on the transfer list."));
			return 0;
		}
		if (FirstOf(menu.Entries, ListingAction) is not { } entry || !IsEnabled(entry))
		{
			await Fm.Get("actions", ("close", "1"));
			Console.Error.WriteLine(L($"{name} has no transfer status to set (he may not be ours)."));
			return 1;
		}
		var pressed = await ChooseEntry(entry, menu.Entries);
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed);

		JsonElement list = default, confirm = default, cancel = default;
		async Task<bool> Window()
		{
			list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().FirstOrDefault(d => Field(d, "Binds") == ListingBinds);
			var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
			confirm = buttons.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxConfirmChanges", StringComparison.Ordinal));
			cancel = buttons.FirstOrDefault(b => Field(b, "Events").Contains("EventFmxRevert", StringComparison.Ordinal));
			return list.ValueKind == JsonValueKind.Object && confirm.ValueKind == JsonValueKind.Object && cancel.ValueKind == JsonValueKind.Object;
		}
		// The list is drawn before the session fills it: its values are the sign it is ready.
		async Task<bool> Filled() => await Window() && list.TryGetProperty("Values", out var vs) && vs.GetArrayLength() > 0;
		if (!await Until(Filled, seconds: 8, every: 0.25))
		{
			Console.Error.WriteLine(L("The transfer status window did not open."));
			return 1;
		}
		async Task<int> Leave(int code)
		{
			await Fm.Get("press", ("ptr", Field(cancel, "Pointer")));
			await Until(async () => !await Window(), seconds: 4, every: 0.2);
			return code;
		}

		var values = list.TryGetProperty("Values", out var v) ? v.EnumerateArray().Select(x => x.ToString()).ToList() : [];
		var shown = list.TryGetProperty("Shown", out var s) ? s.EnumerateArray().Select(x => x.ToString()).ToList() : [];
		var at = values.IndexOf(wanted.ToString());
		if (at < 0)
		{
			Console.Error.WriteLine(L($"The game offers no such transfer status for {name}: {string.Join(" · ", shown)}"));
			return await Leave(1);
		}
		var said = at < shown.Count ? shown[at] : "";
		// the game asks nothing here, and it is undone the same way: no question of our own either
		if (!json) Console.WriteLine(Dim($"{name} → {said}"));

		var chosen = await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", wanted.ToString()));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) { await Leave(1); return Report(chosen); }
		// The list writes the value back as its selection: the game's sign the session took it.
		if (!await Until(async () => await Window() && int.TryParse(Field(list, "Selected"), out var i) && i >= 0 && i < values.Count && values[i] == wanted.ToString(), seconds: 4, every: 0.2))
		{
			Console.Error.WriteLine(L("The transfer status could not be chosen."));
			return await Leave(1);
		}
		await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
		await Until(async () => !await Window(), seconds: 6, every: 0.2);

		// Read back: the game often says yes and does nothing.
		var listed = false;
		var landed = await Until(async () => (listed = await OnList() == true) == !off, seconds: 6, every: 0.5);
		if (!landed)
		{
			Console.Error.WriteLine(listed ? L($"The game did not take it: {name} is still on the transfer list.") : L($"The game did not take it: {name} is not on the transfer list."));
			return 1;
		}
		if (json) Console.WriteLine(JsonOut.Of(new { Name = name, Listed = listed }));
		else Console.WriteLine(off ? L($"{name} is off the transfer list.") : L($"{name} is on the transfer list."));
		return 0;
	}
}
