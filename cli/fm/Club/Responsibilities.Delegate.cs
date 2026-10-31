using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Hands a duty to someone in particular: the duty's AssignableStaffList, one of whom is put at its
	/// StaffMemberInCharge (what the page's list of people writes), then confirmed.
	/// </summary>
	private static async Task<int> GiveDuty(string session, List<string> words)
	{
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: responsibilities give \"<duty>\" [<person>]"));
			return 64;
		}
		if (await Unconfirmed(session)) return 1;
		if (await FindDuty(session, words[0]) is not { } duty) return 64;
		var title = duty.Title;
		if (!duty.CanChange)
		{
			Console.Error.WriteLine(L($"{title}: locked, the game does not let it be given to someone else ({duty.Holder})."));
			return 1;
		}

		if (!await DutyTab(session, duty) || await DutyAgain(session, duty) is not { } fresh)
		{
			Console.Error.WriteLine(L($"{title}: not found on the page."));
			return 1;
		}
		duty = fresh;
		var staff = (await Fm.Pull(duty.Row + ".AssignableStaffList", "Name,UniqueId", list: true)) is { ValueKind: JsonValueKind.Array } list
			? list.EnumerateArray().ToList() : [];
		var people = staff.Select(p => Field(p, "Name")).ToList();
		if (people.Count == 0 || people.Any(p => p.Length == 0))
		{
			Console.Error.WriteLine(L($"{title}: who it can be given to could not be read."));
			return 1;
		}

		string person;
		if (words.Count > 1)
		{
			if (Choose(people, string.Join(" ", words.Skip(1)), title) is not { } picked) return 1;
			person = picked;
		}
		else if (Ui.Interactive)
		{
			if (Ui.Ask(title, people, current: staff.FindIndex(p => Field(p, "UniqueId") == duty.HolderId), cancel: L("Cancel")) is not { } picked) return 0;
			person = people[picked];
		}
		else
		{
			Console.WriteLine(L($"{title}: {duty.Holder}\nCan be given to: {string.Join(" · ", people)}"));
			return 0;
		}
		var chosen = staff[people.IndexOf(person)];
		var id = Field(chosen, "UniqueId");
		if (id == duty.HolderId)
		{
			Console.WriteLine(L($"{title}: already {person}."));
			return 0;
		}

		var put = await Fm.Get("pathset", ("to", duty.Row + ".StaffMemberInCharge"), ("from", Field(chosen, "Row")));
		if (put.StartsWith("error", StringComparison.Ordinal)) return Report(put);

		Duty? after = null;
		await Until(async () => (after = await DutyAgain(session, duty))?.HolderId == id, seconds: 6, every: 0.3);
		if (after?.HolderId != id)
		{
			await Revert(session);
			Console.Error.WriteLine(L($"{title}: {person} was chosen, but the duty is still {after?.Holder ?? duty.Holder}."));
			return 1;
		}
		if (await ConfirmDuties(session) is { } failed) return failed;
		after = await DutyAgain(session, duty);
		if (after?.HolderId != id)
		{
			Console.Error.WriteLine(L($"{title}: {person} was chosen, but the duty is still {after?.Holder ?? duty.Holder}."));
			return 1;
		}
		Console.WriteLine($"{title}: {duty.Holder} → {after.Holder}");
		return 0;
	}

	/// <summary>Takes a duty back or hands it on, with the event the page's pair of buttons sends, then confirmed.</summary>
	private static async Task<int> SetDuty(string session, bool take, string typed)
	{
		if (typed.Length == 0)
		{
			Console.Error.WriteLine(L($"Which duty?  responsibilities {(take ? "take" : "delegate")} \"<duty>\""));
			return 64;
		}
		if (await Unconfirmed(session)) return 1;
		if (await FindDuty(session, typed) is not { } duty) return 64;
		var title = duty.Title;
		if (duty.Mine == take)
		{
			Console.WriteLine(L($"{title}: already {(take ? L("yours") : L($"delegated ({duty.Holder})"))}."));
			return 0;
		}
		if (take ? !duty.CanTake : !duty.CanDelegate)
		{
			Console.Error.WriteLine(L($"{title}: the game {(take ? L("does not let it be taken back") : L("does not let it be delegated"))} (the button is closed)."));
			return 1;
		}

		if (!await DutyTab(session, duty) || await DutyAgain(session, duty) is not { } fresh)
		{
			Console.Error.WriteLine(L($"{title}: not found on the page."));
			return 1;
		}
		var sent = await Fm.Get("send", ("event", "EventToPlugin"), ("ID", "EventFmxResponsibilityTakeOrDelegateControl"), ("Target", fresh.Row));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);

		Duty? after = null;
		await Until(async () => (after = await DutyAgain(session, duty))?.Mine == take, seconds: 6, every: 0.3);
		if (after?.Mine != take)
		{
			await Revert(session);
			Console.Error.WriteLine(L($"{title}: the button was pressed, but nothing changed."));
			return 1;
		}
		if (await ConfirmDuties(session) is { } failed) return failed;
		after = await DutyAgain(session, duty);
		if (after?.Mine != take)
		{
			Console.Error.WriteLine(L($"{title}: the button was pressed, but nothing changed."));
			return 1;
		}
		Console.WriteLine(take ? L($"{title}: now yours.") : L($"{title}: delegated → {after.Holder}"));
		return 0;
	}

	/// <summary>
	/// True (and said) when the session holds changes made on the page and not confirmed: the
	/// Confirm that follows a change here would put those in too.
	/// </summary>
	private static async Task<bool> Unconfirmed(string session)
	{
		if (Field(await Fm.Pull(session, "CanRevert"), "CanRevert") != "True") return false;
		Console.Error.WriteLine(L("The responsibilities page has changes that were not confirmed; confirm or undo them in the game first."));
		return true;
	}

	/// <summary>Drops what was changed in the session and not confirmed (the page's Reset).</summary>
	private static async Task Revert(string session)
	{
		await Fm.Get("send", ("event", "EventToPlugin"), ("ID", "EventFmxRevert"), ("Target", session));
		await Until(async () => Field(await Fm.Pull(session, "CanRevert"), "CanRevert") == "False", seconds: 5, every: 0.25);
	}

	/// <summary>
	/// Confirms the session's changes, as the page's Confirm does; done once the session has nothing
	/// left to revert. A question the game asks is said and left open for `dialog`.
	/// </summary>
	private static async Task<int?> ConfirmDuties(string session)
	{
		var sent = await Fm.Get("send", ("event", "EventToPlugin"), ("ID", "EventFmxResponsibilitySettingsChangeConfirm"), ("Target", session));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
		var done = await Until(async () =>
		{
			if ((await Fm.GetJson("dialog")).GetArrayLength() > 0) return true;
			return Field(await Fm.Pull(session, "CanRevert"), "CanRevert") == "False";
		}, seconds: 8, every: 0.3);

		var asked = await Fm.GetJson("dialog");
		if (asked.GetArrayLength() > 0)
		{
			Console.Error.WriteLine(L($"The game asks: {DialogWords(asked[0], "Title")} — {DialogWords(asked[0], "Description")}   (to answer: dialog)"));
			return 1;
		}
		if (!done)
		{
			Console.Error.WriteLine(L("The change was made but the game did not take the confirmation."));
			return 1;
		}
		return null;
	}
}
