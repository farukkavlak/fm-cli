using FmCli;
using Xunit;

namespace FmCli.Tests;

public class RadarMotionTests
{
	private static Program.Man Man(int entity, double x, double y) => new(entity, x, y, "005798", false, 0, "f8f8f8", entity);

	private static Program.RadarFrame Frame(double ballX, params Program.Man[] men) => new(men, ballX, 0.5, 0);

	private static readonly DateTime Start = new(2029, 4, 29, 15, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void HalfwayBetweenTwoReadingsTheMenAreHalfwayThere()
	{
		var blend = RadarMotion.Blend(Frame(0.4, Man(7, 0.2, 0.5)), Frame(0.6, Man(7, 0.3, 0.5)), 0.5);
		Assert.Equal(0.25, blend.Men[0].X, 6);
		Assert.Equal(0.5, blend.BallX, 6);
	}

	[Fact]
	public void MenAreMatchedByEntityNotByPlace()
	{
		var before = Frame(0.5, Man(7, 0.2, 0.5), Man(9, 0.8, 0.5));
		var after = Frame(0.5, Man(9, 0.7, 0.5), Man(7, 0.3, 0.5));
		var blend = RadarMotion.Blend(before, after, 0.5);
		Assert.Equal(0.75, blend.Men.Single(m => m.Entity == 9).X, 6);
		Assert.Equal(0.25, blend.Men.Single(m => m.Entity == 7).X, 6);
	}

	[Fact]
	public void AManWhoJumpsIsPutDownAtOnce()
	{
		// A substitute from the bench (y = 1) to his place, or the camera cutting to another highlight.
		var blend = RadarMotion.Blend(Frame(0.5, Man(7, 0.5, 1.0)), Frame(0.5, Man(7, 0.5, 0.4)), 0.5);
		Assert.Equal(0.4, blend.Men[0].Y, 6);
		// and one who only ran on is not
		var ran = RadarMotion.Blend(Frame(0.5, Man(7, 0.5, 0.6)), Frame(0.5, Man(7, 0.5, 0.4)), 0.5);
		Assert.Equal(0.5, ran.Men[0].Y, 6);
	}

	[Fact]
	public void AManWhoIsNewIsDrawnWhereHeIs()
	{
		var blend = RadarMotion.Blend(Frame(0.5, Man(7, 0.2, 0.5)), Frame(0.5, Man(7, 0.3, 0.5), Man(12, 0.6, 0.6)), 0.5);
		Assert.Equal(0.6, blend.Men.Single(m => m.Entity == 12).X, 6);
	}

	[Fact]
	public void ANewReadingStartsFromWhatWasDrawnSoNothingJumpsBack()
	{
		var motion = new RadarMotion();
		motion.Add(Frame(0.5, Man(7, 0.0, 0.5)), Start);
		motion.Add(Frame(0.5, Man(7, 0.1, 0.5)), Start.AddSeconds(0.3));
		// Halfway through the walk the next reading comes early.
		var shown = motion.At(Start.AddSeconds(0.45))!.Men[0].X;
		Assert.InRange(shown, 0.04, 0.06);
		motion.Add(Frame(0.5, Man(7, 0.2, 0.5)), Start.AddSeconds(0.45));
		Assert.Equal(shown, motion.At(Start.AddSeconds(0.45))!.Men[0].X, 6);
		// and ends at the newest reading, never past it
		Assert.Equal(0.2, motion.At(Start.AddSeconds(5))!.Men[0].X, 6);
	}

	[Fact]
	public void WithOneReadingThatIsWhatIsDrawn()
	{
		var motion = new RadarMotion();
		Assert.Null(motion.At(Start));
		var frame = Frame(0.5, Man(7, 0.2, 0.5));
		motion.Add(frame, Start);
		Assert.Same(frame, motion.At(Start.AddSeconds(0.1)));
	}
}
