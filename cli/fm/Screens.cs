namespace FmCli;

/// <summary>
/// The pages commands open, by the GUID the game's own files give each one (`fm pages` lists
/// them). The GUID is the same in every language and every save — the home page's was the same
/// in the first Fenerbahçe save as in Lincoln's — where the page's name is whatever the game's
/// language calls it. The names here are only for reading the code.
///
/// A panel that stands for several teams (First Team, Under 21s and Under 18s are one) is told
/// which by the team's unique id: see <see cref="Program.OpenPage"/>.
/// </summary>
internal static class Screen
{
	// Portal
	public const string Home = "2d1049f4571df444eb050b39a5b8aaec";
	public const string Calendar = "373351a1f7249424eb22205fc799d1b9";
	public const string NewsSite = "0808ae564ebb73744b1ab884b5239fa8";
	public const string Stages = "c705e9f71253e1c4a8e1259ced17f11a";
	/// <summary>Portal › Opposition Report and Match Day › Next Opposition Report: one page in two menus.</summary>
	public const string OppositionReport = "b96211c5068254e4287314ac009797f1";

	// Squad
	/// <summary>A team's squad list; the club's own team unless another is named.</summary>
	public const string Squad = "7957386accba7b44d81ac248d04efbac";
	public const string Training = "8d64c97dd31aa8e4cab1d3fc91c7fc91";
	public const string YouthSetup = "fdc4ca40b0312734b9a99317f9d351d0";
	public const string Dynamics = "7d9c93953e66d3d468a7acd301e08618";
	// Squad Dynamics' own subtabs, opened from its bar: see <see cref="Program.OpenSubpage"/>.
	public const string Captains = "f60867965de3a7f4fbe75b641bee20af";
	public const string Hierarchy = "5c93feb8d01d89840a52ec0ef3d8cf5f";
	public const string SocialGroups = "f0783a3c26287104a9d968eb2f2f43ae";
	public const string Happiness = "236ce198f4ade4a4ea0f33b6fa94937c";
	public const string Registration = "2711315fec49f2e4cbcbcadd361774b2";

	// Recruitment
	public const string RecruitmentHome = "fba8a55c175acc34d9b0eb9ea1eb6671";
	public const string PlayerDatabase = "bfe5117deac3c454f8970018733ac664";
	public const string StaffDatabase = "a6331fde9dbe9fa47a9da59a7fc09715";
	public const string RecruitmentFocuses = "c914d186dea95f44a997f5d3609862c7";
	public const string RecruitmentBudgets = "9bef185e04bef64439c4821d40581882";
	public const string TransferActivity = "ab4d79ab8c8ef994d967c50b148bacb6";

	// Match Day
	public const string MatchDayHome = "5fca83a7c7c29c14ab624fcb3ef707f9";
	public const string Tactics = "a69d2d19d0ea8054b82d191ecfea01be";
	public const string SetPieces = "f222c8c1e9d78bd4e88e70cc14affc4d";
	public const string Penalties = "5a5e958112d5df14792774bdd0a43e1e";
	public const string OppositionInstructions = "f3a5a1da7cb15294e8f54828aad36c42";

	// Club
	public const string ClubHome = "92399575743a8cb4885207bb0c24e75e";
	public const string Board = "faf77e4c3bf09ef4c8312e637c0018a8";
	public const string Responsibilities = "5cb9ef71c6606fd488afd48f21597843";

	// Career
	public const string ManagerialVacancies = "5b0a6d8141dcf4d4d984c668a4a94404";
}
