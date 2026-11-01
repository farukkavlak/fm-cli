namespace FmCli;

internal static partial class Program
{
	private static List<string> PickedAbout = [];

	private static void SayAbout()
	{
		foreach (var line in PickedAbout)
		{
			if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(line)}[/]");
			else Console.WriteLine($"  {line}");
		}
	}
}
