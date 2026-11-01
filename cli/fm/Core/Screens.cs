namespace FmCli;

/// <summary>
/// The pages commands open, by the GUID the game's own files give each one (`fm pages` lists
/// them). The GUID is the same in every language and every save — the home page's was the same
/// on one save as on another — where the page's name is whatever the game's
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
	/// <summary>Portal › Calendar › Go On Holiday.</summary>
	public const string Holiday = "7ee51df42b0a89646a12ba40c2446ee5";
	public const string NewsSite = "2d4ceb2449af95b49bfc0f89c6403627";
	public const string Stages = "c705e9f71253e1c4a8e1259ced17f11a";
	/// <summary>A competition's own page (Portal › Competitions › the league); one panel for every competition.</summary>
	public const string Competition = "851a9406c4fc6964c9db74baa71407b7";

	/// <summary>
	/// Pages listed only on another page's bar, which no tab menu lists (FM27: Stages is under the
	/// league page's "Stages ›"). <see cref="Program.OpenPage"/> opens the page first, then chooses there.
	/// </summary>
	public static readonly Dictionary<string, string> OnBarOf = new() { [Stages] = Competition };
	/// <summary>Portal › Opposition Report and Match Day › Next Opposition Report: one page in two menus.</summary>
	public const string OppositionReport = "b96211c5068254e4287314ac009797f1";

	// Squad
	/// <summary>A team's squad list; the club's own team unless another is named.</summary>
	public const string Squad = "7957386accba7b44d81ac248d04efbac";
	public const string Training = "ceb0e5333c8c69c448f04279129fdb38";
	// Training's own subtabs, opened from its bar.
	public const string IndividualTraining = "41012275212b88b46957d71fc17c5876";
	public const string Mentoring = "d4f349f49047bdb47a53216fa71298f3";
	/// <summary>The mentoring group editor that "Create New Mentoring Group" opens over Mentoring.</summary>
	public const string MentoringEditor = "57590a1a52e2c334485fd4134fa6e0cc";
	public const string TrainingIntensity = "6f6099935b22261459f76dc63f312b14";
	public const string CoachAssignments = "b134c6823f6d1784c97dfd072102c8dc";
	public const string YouthSetup = "fdc4ca40b0312734b9a99317f9d351d0";
	// Youth Setup's own subtabs, opened from its bar.
	public const string YouthEnvironment = "1ebaf239acfff9f469dfd35b2a14b3a1";
	public const string YouthPerformance = "80bb01289cbbf7748b70ebae7ce8f477";
	public const string YouthCandidates = "e88d1df476224bc4aa5751e7ff122497";
	public const string Dynamics = "7d9c93953e66d3d468a7acd301e08618";
	// Squad Dynamics' own subtabs, opened from its bar: see <see cref="Program.OpenSubpage"/>.
	public const string Captains = "f60867965de3a7f4fbe75b641bee20af";
	public const string Hierarchy = "5c93feb8d01d89840a52ec0ef3d8cf5f";
	public const string SocialGroups = "f0783a3c26287104a9d968eb2f2f43ae";
	public const string Happiness = "236ce198f4ade4a4ea0f33b6fa94937c";
	public const string Registration = "2711315fec49f2e4cbcbcadd361774b2";
	/// <summary>Squad › First Team › Squad Numbers (FM27: a page of the first team's submenu).</summary>
	public const string SquadNumbers = "4b39af3b111fcfd4f84f4d1fa74ceaf9";

	// Recruitment
	public const string RecruitmentHome = "fba8a55c175acc34d9b0eb9ea1eb6671";
	/// <summary>Club › Supporters (FM27: a subtab of the Club tab; it was a card behind a tile).</summary>
	public const string SupportersPage = "0bc05f9601b07db468b6ec7078a417db";
	/// <summary>Transfers › Shortlists (FM27: a row of the Transfers menu of its own).</summary>
	public const string Shortlists = "6eb3e66627a5fe7469f9d179885acaa9";
	public const string PlayerDatabase = "bfe5117deac3c454f8970018733ac664";
	public const string StaffDatabase = "a6331fde9dbe9fa47a9da59a7fc09715";
	public const string RecruitmentFocuses = "c914d186dea95f44a997f5d3609862c7";
	public const string RecruitmentBudgets = "82e7ca1826bc7fe47a18861a62b45d69";
	public const string TransferActivity = "ab4d79ab8c8ef994d967c50b148bacb6";

	// Match Day
	public const string MatchDayHome = "5fca83a7c7c29c14ab624fcb3ef707f9";
	public const string Tactics = "a69d2d19d0ea8054b82d191ecfea01be";
	/// <summary>Career › My Contract: Resign, Retire and Request New Contract live here.</summary>
	public const string MyContract = "00f51e1cd2f1bfd46806c90cabb6da7f";
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
