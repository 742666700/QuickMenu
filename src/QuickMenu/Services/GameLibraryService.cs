using System.Text.Json;
using System.Text.RegularExpressions;
using QuickMenu.Models;
using QuickMenu.Services.GameSources;

namespace QuickMenu.Services;

/// <summary>
/// Collects installed games so they appear in the menu even when nobody put a shortcut on the desktop: the install
/// records of Steam, Epic, GOG, Ubisoft Connect, the Xbox app and other launchers, plus folders and games the user
/// added in settings (game-library.json). Results are cached for five minutes; F5 or "重新扫描" rescans.
/// </summary>
public sealed class GameLibraryService(string? configurationPath = null)
{
    public static string DefaultConfigurationPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickMenu", "game-library.json");

    /// <summary>The sources in the order settings shows them, with their names there (in the current language).</summary>
    public static IReadOnlyList<(string Id, string Name)> Sources =>
        new[] { "Steam", "Epic", "GOG", "Ubisoft", "Xbox", "Installed", "Folders" }.Select(id => (id, Loc.T("Source." + id))).ToArray();

    // Tools shipped next to games: installers, redistributables, launchers, config tools and the like.
    private static readonly Regex SkippedExecutable = new(@"unins|setup|install|redist|config|savefolder|python|zsync|helper|launcher|^startup$|^start_protected_game$|修改|安装|卸载|补丁|开始游戏", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // These mark tools too but turn up inside game names (Dispatch, Ghost of Tsushima, Crash Bandicoot, Downloader), so they
    // count only as whole words of the name split at case changes, digits and separators (UnityCrashHandler64 → unity crash
    // handler 64), and "crash" only when a crash tool follows.
    private static readonly Regex SkippedWord = new(@"\bpatch\w*|\bhost\b|\bloader\b|\bcrash(pad|handler|report|sender|dump)\w*|\bcrash\w* (handler|report\w*|sender|dump\w*)\b", RegexOptions.Compiled);
    private static readonly Regex WordBreak = new(@"(?<=\p{Ll})(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})|(?<=\p{L})(?=\p{N})|(?<=\p{N})(?=\p{L})", RegexOptions.Compiled);
    private static readonly Regex Separators = new(@"[^\p{L}\p{N}]+", RegexOptions.Compiled);
    private static readonly Regex SkippedFolder = new(@"backup|rollback|quarantine|备份|回滚|修改器|^\.|^(lib|renpy|node_modules|Redist|EasyAntiCheat|me3|amd-nr-runtime|dlssnr-amd|data|plugins?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static bool IsSkippedExecutable(string stem) =>
        SkippedExecutable.IsMatch(stem) || SkippedWord.IsMatch(Separators.Replace(WordBreak.Replace(stem, " "), " ").Trim().ToLowerInvariant());
    internal static bool IsSkippedFolder(string name) => name.StartsWith('_') || SkippedFolder.IsMatch(name);

    private readonly string _path = configurationPath ?? DefaultConfigurationPath;
    private IReadOnlyList<GameEntry>? _cached;
    private DateTime _scanned;
    private volatile IReadOnlyDictionary<string, int> _counts = new Dictionary<string, int>();

    private volatile IReadOnlyDictionary<string, string> _names = new Dictionary<string, string>();

    /// <summary>How many games each source contributed in the last scan, before hidden games are taken out.</summary>
    public IReadOnlyDictionary<string, int> Counts => _counts;

    /// <summary>A game's name from the last scan by its launch target, so settings can name hidden games.</summary>
    public string? NameOf(string launch) => _names.GetValueOrDefault(launch);

    public void Invalidate() => _cached = null;

    public IReadOnlyList<GameEntry> GetGames(CancellationToken token)
    {
        if (_cached != null && DateTime.UtcNow - _scanned < TimeSpan.FromMinutes(5)) return _cached;
        var settings = Load();
        var sources = new List<IGameSource> { new SteamSource(), new EpicSource(), new GogSource(), new UbisoftSource(), new XboxSource(), new InstalledProgramsSource(), new FolderSource(settings.Folders) };
        var found = new List<GameEntry>();
        foreach (var source in sources.Where(source => settings.IsEnabled(source.Id)))
        {
            // A store whose records are missing or unreadable must not hide the games of the others.
            try { found.AddRange(source.Find(token)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { App.Log(ex); }
        }
        var (games, counts) = Merge(found, settings);
        token.ThrowIfCancellationRequested();
        _counts = counts;
        _names = found.GroupBy(game => game.Launch, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
        _cached = games; _scanned = DateTime.UtcNow;
        return games;
    }

    /// <summary>
    /// Puts the sources together: a game found twice (by launch link, exe or install folder) is listed once, the
    /// first source winning; games added by hand join; hidden games leave.
    /// </summary>
    public static (IReadOnlyList<GameEntry> Games, IReadOnlyDictionary<string, int> Counts) Merge(IEnumerable<GameEntry> found, GameLibrarySettings settings)
    {
        var games = new List<GameEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in found.Concat(settings.Added.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Added)))
        {
            var keys = new[] { game.Launch, game.Exe }.OfType<string>().ToArray();
            if (keys.Any(seen.Contains)) continue;
            foreach (var key in keys) seen.Add(key);
            games.Add(game);
        }
        var counts = games.GroupBy(game => game.Source).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var hidden = settings.Hidden.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (games.Where(game => !hidden.Contains(game.Launch)).OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), counts);
    }

    private static GameEntry Added(string path) => new(path, Path.GetFileNameWithoutExtension(path),
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path : null, Path.GetDirectoryName(path), "Added");

    /// <summary>
    /// Reads game-library.json. Before 2.0 it was a plain list of folders, which becomes <see cref="GameLibrarySettings.Folders"/>.
    /// A missing or unreadable file means the defaults: every store on, no extra folders.
    /// </summary>
    public GameLibrarySettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            var json = File.ReadAllText(_path);
            if (json.TrimStart().StartsWith('[')) return new() { Folders = JsonSerializer.Deserialize<List<string>>(json) ?? [] };
            var settings = JsonSerializer.Deserialize<GameLibrarySettings>(json) ?? new();
            settings.Sources = new(settings.Sources ?? [], StringComparer.OrdinalIgnoreCase);
            settings.Folders ??= []; settings.Added ??= []; settings.Hidden ??= [];
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { App.Log(ex); return new(); }
    }

    public OperationResult Save(GameLibrarySettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            File.Move(temporary, _path, overwrite: true);
            Invalidate();
            return new(true, Loc.T("Games.Updated"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); return new(false, Loc.T("Games.SaveFailed") + ex.Message); }
    }

    /// <summary>
    /// Walks each root (up to ten levels, skipping links, backups and runtime folders) and keeps executables that sit
    /// next to a recognisable game engine: Unity, Unreal, Godot, GameMaker, RPG Maker, Ren'Py, KiriKiri, AliceSoft or
    /// Steamworks.
    /// </summary>
    public static IReadOnlyList<string> Scan(IEnumerable<string> roots, CancellationToken token = default)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root))) Visit(root, 0);
        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        void Visit(string directory, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 9 || !Directory.Exists(directory) || !visited.Add(Path.GetFullPath(directory))) return;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                var files = Directory.GetFiles(directory);
                var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool Folder(string name) => Directory.Exists(Path.Combine(directory, name));
                foreach (var exe in files.Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    var stem = Path.GetFileNameWithoutExtension(exe);
                    if (IsSkippedExecutable(stem)) continue;
                    var rpgMaker = stem.Equals("Game", StringComparison.OrdinalIgnoreCase) && (Folder("www") || (Folder("js") && Folder("data")) || names.Contains("Game.ini"));
                    var engine = Folder(stem + "_Data")
                        || (Folder("renpy") && Folder("game"))
                        || names.Contains("data.xp3") || names.Contains(stem + ".ain")
                        || names.Contains("steam_api.dll") || names.Contains("steam_api64.dll")
                        || stem.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)
                        || names.Contains(stem + ".pck") || names.Contains("data.win") || rpgMaker;
                    // Ren'Py ships Game.exe and Game-32.exe side by side; keep one.
                    var duplicate32 = stem.EndsWith("-32", StringComparison.OrdinalIgnoreCase) && names.Contains(stem[..^3] + ".exe");
                    if (engine && !duplicate32) result.Add(exe);
                }
                foreach (var child in Directory.GetDirectories(directory))
                    if (!IsSkippedFolder(Path.GetFileName(child))) Visit(child, depth + 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* An unreadable folder must not hide other games. */ }
        }
    }
}
