using System.Security.Cryptography;

namespace FmCli;

/// <summary>
/// The language model behind sentences is too large for the binary (~280 MB), so the install
/// (`fm doctor --install`, which tools/install.sh runs last) downloads it once into
/// ~/.cache/fm-cli/nl/&lt;name&gt;/ and points ~/.cache/fm-cli/nl/model at it. Each file is checked
/// against its SHA-256 before it is kept. A new model gets a new name, so the next install
/// fetches it and the old folder can go.
/// </summary>
internal static partial class Program
{
	private const string NlModelName = "mpnet-n-q";

	/// <summary>
	/// Where the model's files are served from: a release of this repository per model,
	/// made by tools/nl-release.sh. FM_NL_URL overrides it.
	/// </summary>
	private const string NlModelUrl = $"https://github.com/farukkavlak/fm-cli/releases/download/nl-{NlModelName}";

	private static readonly (string File, long Size, string Sha256)[] NlModelFiles =
	[
		("model.onnx", 278894592, "eb15276e9c281fa79af43c794d6802c822c21d6750ce647c11ec8357868aa7ae"),
		("tokenizer.json", 9081518, "2c3387be76557bd40970cec13153b3bbf80407865484b209e655e5e4729076b8"),
	];

	private static string NlRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "fm-cli", "nl");

	private static bool NlModelInstalled =>
		Environment.GetEnvironmentVariable("FM_NL_MODEL") is { Length: > 0 } dir
			? File.Exists(Path.Combine(dir, "model.onnx"))
			: NlModelFiles.All(f => new FileInfo(Path.Combine(NlRoot, NlModelName, f.File)) is { Exists: true } i && i.Length == f.Size)
			  && File.Exists(Path.Combine(NlRoot, "model", "model.onnx"));

	/// <summary>Downloads the model unless it is there; false when it could not be had.</summary>
	private static async Task<bool> EnsureNlModel()
	{
		if (NlModelInstalled) return true;
		if (Environment.GetEnvironmentVariable("FM_NL_MODEL") is { Length: > 0 }) return false;
		var url = Environment.GetEnvironmentVariable("FM_NL_URL") is { Length: > 0 } u ? u : NlModelUrl;
		var dir = Path.Combine(NlRoot, NlModelName);
		Directory.CreateDirectory(dir);
		Console.WriteLine(L($"Downloading the language model for sentences (~{NlModelFiles.Sum(f => f.Size) / 1_000_000} MB, once)…"));
		using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		foreach (var (file, size, sha) in NlModelFiles)
		{
			var target = Path.Combine(dir, file);
			if (new FileInfo(target) is { Exists: true } have && have.Length == size) continue;
			var part = target + ".part";
			try
			{
				using (var response = await http.GetAsync($"{url.TrimEnd('/')}/{file}", HttpCompletionOption.ResponseHeadersRead))
				{
					response.EnsureSuccessStatusCode();
					await using var from = await response.Content.ReadAsStreamAsync();
					await using var to = File.Create(part);
					var buffer = new byte[1 << 16];
					long done = 0, shown = -1;
					int read;
					while ((read = await from.ReadAsync(buffer)) > 0)
					{
						await to.WriteAsync(buffer.AsMemory(0, read));
						done += read;
						var percent = done * 100 / size;
						if (Ui.Rich && percent != shown) Console.Write($"\r  {file} {(shown = percent),3}%");
					}
					if (Ui.Rich) Console.WriteLine();
				}
				await using (var check = File.OpenRead(part))
					if (Convert.ToHexString(await SHA256.HashDataAsync(check)).ToLowerInvariant() != sha)
					{
						File.Delete(part);
						Console.Error.WriteLine(L($"The downloaded {file} is not the one expected; try again later."));
						return false;
					}
				File.Move(part, target, overwrite: true);
			}
			catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
			{
				Console.Error.WriteLine(L($"Could not download the language model: {e.Message}"));
				return false;
			}
		}

		var link = Path.Combine(NlRoot, "model");
		if (Directory.Exists(link) || File.Exists(link))
		{
			if (new DirectoryInfo(link).LinkTarget == null)
			{
				Console.Error.WriteLine(L($"{link} is a folder, not a link; the new model is in {dir}."));
				return false;
			}
			Directory.Delete(link);
		}
		Directory.CreateSymbolicLink(link, NlModelName);
		Console.WriteLine(L("Language model ready: sentences work in place of commands."));
		return true;
	}
}
