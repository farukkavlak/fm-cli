using System.Text.RegularExpressions;
using FmCli;
using Xunit;

namespace FmCli.Tests;

public class RadarStylesTests
{
	private static Program.RadarFrame Frame(string homeKit = "005798", string awayKit = "f8f8f8")
	{
		var men = new List<Program.Man> { new(1, 0.08, 0.5, "d0d008", false, 0, "080808") };
		for (var i = 0; i < 10; i++) men.Add(new(i + 2, 0.2 + i * 0.05, 0.1 + i * 0.08, homeKit, false, 0, "f8f8f8"));
		men.Add(new(22, 0.92, 0.5, "10b850", false, 1, "080808"));
		for (var i = 0; i < 10; i++) men.Add(new(i + 2, 0.4 + i * 0.05, 0.9 - i * 0.08, awayKit, false, 1, "d00817"));
		return new Program.RadarFrame(men, 0.5, 0.5, 0);
	}

	private static readonly Program.RadarSides Sides = new("Empoli", "Venezia", true);

	private static string Plain(string line) => Regex.Replace(line, "\x1b\\[[0-9;]*m", "");

	[Theory]
	[InlineData("pixel", "pixel")]
	[InlineData("retro", "pixel")]
	[InlineData("Braille", "braille")]
	[InlineData("color", "colour")]
	[InlineData("klasik", "ascii")]
	[InlineData("auto", "auto")]
	[InlineData("izometrik", "iso")]
	[InlineData("raycast", "raycast")]
	[InlineData("voksel", "voxel")]
	public void ParsesStyleNames(string text, string expected) =>
		Assert.Equal(expected, Program.ParseRadarStyle(text));

	[Fact]
	public void RejectsUnknownStyles() => Assert.Null(Program.ParseRadarStyle("3d"));

	[Theory]
	[InlineData("pixel", 100, 40)]
	[InlineData("pixel", 160, 30)]
	[InlineData("pixel", 84, 21)]
	[InlineData("braille", 80, 24)]
	[InlineData("braille", 84, 21)]
	[InlineData("colour", 120, 20)]
	[InlineData("colour", 84, 21)]
	[InlineData("ascii", 70, 50)]
	[InlineData("ascii", 84, 21)]
	[InlineData("iso", 120, 40)]
	[InlineData("raycast", 160, 30)]
	[InlineData("voxel", 84, 21)]
	public void FitsTheRoomItIsGiven(string style, int cols, int rows)
	{
		var lines = Program.RadarView(Frame(), Sides, style, true, cols, rows);
		Assert.InRange(lines.Count, 2, rows);
		// sextants lie outside the 16-bit range, two chars each but one cell
		Assert.All(lines, l => Assert.True(Plain(l).EnumerateRunes().Count() <= cols + 2, $"{Plain(l).EnumerateRunes().Count()} > {cols + 2}"));
	}

	[Fact]
	public void TheLowCameraLooksThroughAManRightInFrontOfIt()
	{
		// The ball in the middle puts the raycast eye at (42.5, 50); this man stands 3 m in front of it.
		var frame = Frame();
		var close = frame with { Men = [.. frame.Men, new(30, 44.1 / 105, 47.5 / 68, "ff00ff", false, 0, "f8f8f8")] };
		var without = Program.RadarView(frame, Sides, "raycast", true, 120, 40);
		var with = Program.RadarView(close, Sides, "raycast", true, 120, 40);
		Assert.Equal(without, with);
	}

	[Fact]
	public void SaysSoWhenTheTerminalIsTooSmall()
	{
		var lines = Program.RadarView(Frame(), Sides, "pixel", true, 36, 10);
		Assert.Single(lines);
		Assert.Contains("36×10", Plain(lines[0]));
	}

	[Fact]
	public void WithoutColourItIsPlainAsciiWithOAndX()
	{
		var lines = Program.RadarView(Frame(), Sides, "pixel", false, 80, 30);
		Assert.DoesNotContain(lines, l => l.Contains('\x1b'));
		var text = string.Join("\n", lines);
		Assert.Contains('o', text);
		Assert.Contains('x', text);
	}

	[Fact]
	public void NamesTheSidesByTheirInitials()
	{
		var text = Plain(string.Join("\n", Program.RadarView(Frame(), Sides, "colour", true, 100, 40)));
		Assert.Contains('E', text);
		Assert.Contains('V', text);
	}

	[Fact]
	public void TheSameInitialFallsBackToOAndX()
	{
		var text = Plain(string.Join("\n", Program.RadarView(Frame(), new("Empoli", "Everton", true), "colour", true, 100, 40)));
		Assert.Contains('o', text);
		Assert.Contains('x', text);
	}

	[Fact]
	public void ATooSmallRadarNoteKeepsTheShapePitch()
	{
		Assert.True(MatchLive.ShowShape(true, 11, ["the radar needs at least 40x15"]));
		Assert.True(MatchLive.ShowShape(true, 11, null));
		Assert.False(MatchLive.ShowShape(true, 11, ["a real", "radar"]));
		Assert.False(MatchLive.ShowShape(true, 0, ["the radar needs at least 40x15"]));
		Assert.False(MatchLive.ShowShape(false, 11, ["the radar needs at least 40x15"]));
	}

	// The simulation's list as the game sent it (Empoli–Frosinone, 26 Ekim 2026): each side's eleven,
	// keeper first, then its bench, bench keeper first; 23 a side.
	private static List<(string Kit, bool Sub)> Block(string keeper, string outfield) =>
		[(keeper, false), .. Enumerable.Repeat((outfield, false), 10), (keeper, true), .. Enumerable.Repeat((outfield, true), 11)];

	[Fact]
	public void SidesAreSplitByKitWithKeepersJoiningTheirBlock()
	{
		var list = Block("7f7f7f", "005798").Concat(Block("008030", "f8c820")).ToList();
		var teams = Program.RadarTeams(list);
		Assert.All(teams.Take(23), t => Assert.Equal(0, t));
		Assert.All(teams.Skip(23), t => Assert.Equal(1, t));
	}

	[Fact]
	public void SubstitutionsDoNotMoveMenToTheOtherSide()
	{
		var list = Block("7f7f7f", "005798").Concat(Block("008030", "f8c820")).ToList();
		// One taken off in each eleven, one brought on from each bench.
		list[5] = (list[5].Kit, true);
		list[15] = (list[15].Kit, false);
		list[28] = (list[28].Kit, true);
		list[40] = (list[40].Kit, false);
		var teams = Program.RadarTeams(list);
		Assert.All(teams.Take(23), t => Assert.Equal(0, t));
		Assert.All(teams.Skip(23), t => Assert.Equal(1, t));
	}

	[Fact]
	public void TheSameKitBothSidesSplitsTheListInHalf()
	{
		var list = Block("7f7f7f", "005798").Concat(Block("008030", "005798")).ToList();
		var teams = Program.RadarTeams(list);
		Assert.Equal(0, teams[22]);
		Assert.Equal(1, teams[23]);
	}

	[Theory]
	[InlineData(255, 0, 0, 196)]
	[InlineData(0, 0, 0, 16)]
	[InlineData(128, 128, 128, 244)]
	public void MapsToTheNearest256Colour(byte r, byte g, byte b, int expected) =>
		Assert.Equal(expected, Ansi.Index(new Rgb(r, g, b)));

	[Theory]
	[InlineData(62, 150, 66, 52, 134, 58)]
	[InlineData(10, 42, 22, 13, 50, 27)]
	public void StripesStayApartIn256Colours(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
	{
		var a = new Rgb(r1, g1, b1);
		var b = Ansi.Apart256(a, new Rgb(r2, g2, b2));
		Assert.NotEqual(Ansi.Index(a), Ansi.Index(b));
		Assert.True(a.Distance(b) < 90, $"{b} moved too far from {a}");
	}

	[Theory]
	[InlineData(1, 0x1FB00)]
	[InlineData(20, 0x1FB13)]
	[InlineData(22, 0x1FB14)]
	[InlineData(62, 0x1FB3B)]
	public void NamesSextants(int bits, int codePoint) =>
		Assert.Equal(char.ConvertFromUtf32(codePoint), Cells.SextantGlyph(bits));

	[Fact]
	public void SplitsACellBetweenItsTwoColours()
	{
		// white on the left, black on the right: the right half in the foreground, as the first pixel is background
		var px = new Rgb[3, 2];
		for (var y = 0; y < 3; y++) (px[y, 0], px[y, 1]) = (new Rgb(255, 255, 255), new Rgb(0, 0, 0));
		Assert.Equal("▐", Plain(Cells.Sextants(px)[0]));
	}

	[Fact]
	public void ReadsHexColours()
	{
		Assert.Equal(new Rgb(0, 0x57, 0x98), Rgb.Hex("005798"));
		Assert.Equal(new Rgb(200, 200, 200), Rgb.Hex("nope"));
	}
}
