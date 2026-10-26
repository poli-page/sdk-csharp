using System.Text;
using System.Text.RegularExpressions;

namespace PoliPage.Demo;

// ANSI colors, step banners, and clickable file:// links. Mirrors the helpers
// in sdk-node's `demo/_shared.mjs` so the C# demo prints the same surface.
internal static class Ansi
{
    private static readonly bool UseColor =
        !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") != "1";

    public static string Bold(string s) => UseColor ? $"\x1b[1m{s}\x1b[0m" : s;
    public static string Dim(string s) => UseColor ? $"\x1b[2m{s}\x1b[0m" : s;
    public static string Red(string s) => UseColor ? $"\x1b[31m{s}\x1b[0m" : s;
    public static string Green(string s) => UseColor ? $"\x1b[32m{s}\x1b[0m" : s;
    public static string Yellow(string s) => UseColor ? $"\x1b[33m{s}\x1b[0m" : s;
    public static string Cyan(string s) => UseColor ? $"\x1b[36m{s}\x1b[0m" : s;

    public static void Step(int n, int total, string name)
    {
        Console.WriteLine();
        Console.WriteLine(Cyan(Bold($"[{n}/{total}] {name}")));
    }

    // Format an absolute path as a file:// URL. Modern terminals (Terminal.app,
    // iTerm2, VS Code, Warp, Windows Terminal) render these as clickable links.
    public static string FileLink(string path)
    {
        var absolute = Path.GetFullPath(path);
        return Cyan(new Uri(absolute).AbsoluteUri);
    }
}

// Walks up from the running assembly's directory to find the repo root,
// identified by a `.git` entry or the `PoliPage.sln` file. Used so the demo
// resolves `.env` and the output directory the same way regardless of where
// `dotnet run` is invoked from.
internal static class RepoPaths
{
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, "PoliPage.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repo root (looking for .git/ or PoliPage.sln).");
    }
}

// API key + base URL resolution that mirrors sdk-node's _shared.mjs. Single
// canonical location for the key: `.env` at the repo root. Process env wins
// over the file; if neither has it, prompt the user and append the pasted
// value back to `.env` so subsequent runs are silent.
internal static class EnvFile
{
    private const string ApiKeyVar = "POLI_PAGE_API_KEY";
    private const string BaseUrlVar = "POLI_PAGE_BASE_URL";
    private const string DefaultBaseUrl = "https://api.poli.page";

    public static Dictionary<string, string> Read(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            result[key] = value;
        }
        return result;
    }

    private static void Append(string path, string key, string value)
    {
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var prefix = (existing.Length > 0 && !existing.EndsWith('\n')) ? "\n" : string.Empty;
        File.AppendAllText(path, $"{prefix}{key}={value}\n", Encoding.UTF8);
    }

    public static Uri? ResolveBaseUrl(string repoRoot)
    {
        var fromEnv = Environment.GetEnvironmentVariable(BaseUrlVar);
        if (!string.IsNullOrEmpty(fromEnv))
            return new Uri(fromEnv);

        var fromFile = Read(Path.Combine(repoRoot, ".env"));
        if (fromFile.TryGetValue(BaseUrlVar, out var value) && value.Length > 0)
            return new Uri(value);

        return new Uri(DefaultBaseUrl);
    }

    // The contract's full-key regex, verbatim: `pp_{env}_{32 lowercase hex}` for
    // any environment name. It checks the shape only, never which environment.
    private static readonly Regex FullKeyPattern = new(
        "^pp_(?=[a-z][a-z0-9-]{0,19}_)[a-z][a-z0-9]*(?:-[a-z0-9]+)*_[0-9a-f]{32}$",
        RegexOptions.CultureInvariant);

    // Resolution order:
    //   1. POLI_PAGE_API_KEY in the host shell (wins for CI).
    //   2. POLI_PAGE_API_KEY in `<repoRoot>/.env`.
    //   3. Interactive prompt — paste a sandbox key, check its format, append to `.env`.
    public static string EnsureApiKey(string repoRoot)
    {
        var fromEnv = Environment.GetEnvironmentVariable(ApiKeyVar);
        if (!string.IsNullOrEmpty(fromEnv))
            return fromEnv;

        var envPath = Path.Combine(repoRoot, ".env");
        var fromFile = Read(envPath);
        if (fromFile.TryGetValue(ApiKeyVar, out var saved) && saved.Length > 0)
        {
            Console.WriteLine(Ansi.Dim($"  using {ApiKeyVar} from {envPath}"));
            return saved;
        }

        var rule = Ansi.Dim("  ─────────────────────────────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine(rule);
        Console.WriteLine(Ansi.Bold(Ansi.Yellow($"   No {ApiKeyVar} found.")));
        Console.WriteLine(rule);
        Console.WriteLine();
        Console.WriteLine("   This demo needs a sandbox key (" + Ansi.Cyan("pp_sandbox_…") + ") to talk");
        Console.WriteLine("   to the Poli Page API. Sandbox renders are watermarked and do not");
        Console.WriteLine("   count toward your monthly PDF quota.");
        Console.WriteLine();
        Console.WriteLine("   Use a fresh organization: the demo renders the " + Ansi.Cyan("getting-started"));
        Console.WriteLine("   project every organization gets in its " + Ansi.Cyan("sandbox") + " environment.");
        Console.WriteLine();
        Console.WriteLine(Ansi.Bold("   How to get one:"));
        Console.WriteLine("     1. Sign in at " + Ansi.Cyan("https://app.poli.page"));
        Console.WriteLine("     2. Go to your organization's API keys page:");
        Console.WriteLine("          " + Ansi.Cyan("https://app.poli.page/orgs/{YOUR_ORG}/keys"));
        Console.WriteLine(Ansi.Dim("        (replace {YOUR_ORG} with your org slug — visible in the"));
        Console.WriteLine(Ansi.Dim("         dashboard URL when you're inside your organization)"));
        Console.WriteLine("     3. Create a key in the " + Ansi.Cyan("sandbox") + " environment and copy");
        Console.WriteLine("        the value (starts with " + Ansi.Cyan("pp_sandbox_") + ").");
        Console.WriteLine();
        Console.WriteLine("   Paste it below — we'll save it to " + Ansi.Cyan(".env") + " (repo root) so");
        Console.WriteLine("   future runs pick it up automatically. (You can also set");
        Console.WriteLine("   " + Ansi.Dim(ApiKeyVar) + " in your shell — that wins over the file.)");
        Console.WriteLine();
        Console.Write(Ansi.Bold("   Paste your pp_sandbox_… key") + " (or Ctrl-C to cancel): ");

        var key = (Console.ReadLine() ?? string.Empty).Trim();
        if (!FullKeyPattern.IsMatch(key))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("  " + Ansi.Red("✗") + " That does not look like an API key (expected pp_{env}_ followed by");
            Console.Error.WriteLine("    32 lowercase hex characters, e.g. pp_sandbox_…). Aborting.");
            Console.Error.WriteLine();
            Environment.Exit(1);
        }

        Append(envPath, ApiKeyVar, key);
        Console.WriteLine($"  {Ansi.Green("✔")} saved to {Ansi.Cyan(envPath)}");
        Console.WriteLine();
        return key;
    }
}
