using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal sealed partial class OfferScreen
{
	private static async Task<bool> Settles(Bridge fm, Func<OfferScreen, bool> done, string route) =>
		await Program.Until(async () => await Read(fm, route) is { } now && done(now), seconds: 4, every: 0.2);
}
