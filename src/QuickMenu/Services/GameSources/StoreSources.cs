using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace QuickMenu.Services.GameSources;

/// <summary>
/// Steam: the install path from the registry, every library listed in libraryfolders.vdf, and each library's
/// appmanifest_*.acf. Games start through steam://rungameid/ so Steam handles updates and the overlay.
/// </summary>
public sealed class SteamSource : IGameSource
{
    // Runtimes and redistributables Steam installs alongside games (Steamworks Common Redistributables, Proton, Steam Linux Runtime…).
    private static readonly HashSet<string> Tools = ["228980", "1070560", "1391110", "1493710", "1628350", "1826330", "2180100", "2348590", "2805730"];
    private static readonly Regex ToolName = new(@"^(Proton\b|Steam Linux Runtime|Steamworks Common|SteamVR Performance Test)|\b(Soundtrack|OST|Artbook|Art Book|Dedicated Server)\b", RegexOptions.IgnoreCase);
    public string Id => "Steam";

    public IEnumerable<GameEntry> Find(CancellationToken token)
    {
        var root = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)
            ?? (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string);
        return string.IsNullOrWhiteSpace(root) ? [] : FromRoot(root.Replace('/', '\\'), token);
    }

    public static IEnumerable<GameEntry> FromRoot(string root, CancellationToken token = default)
    {
        var libraries = new List<string> { root };
        var listing = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(listing))
        {
            var folders = Vdf.Parse(File.ReadAllText(listing)).Child("libraryfolders");
            if (folders != null)
                foreach (var library in folders.Values)
                    if (library is Vdf.Node node && node.Text("path") is { Length: > 0 } path) libraries.Add(path);
                    else if (library is string legacy && Path.IsPathFullyQualified(legacy)) libraries.Add(legacy);
        }
        foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                token.ThrowIfCancellationRequested();
                Vdf.Node? state;
                try { state = Vdf.Parse(File.ReadAllText(manifest)).Child("AppState"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (state?.Text("appid") is not { Length: > 0 } id || state.Text("name") is not { Length: > 0 } name || Tools.Contains(id) || ToolName.IsMatch(name)) continue;
                // StateFlags bit 4 means fully installed; a game still downloading has no runnable files yet.
                if (int.TryParse(state.Text("StateFlags"), out var flags) && (flags & 4) == 0) continue;
                var folder = state.Text("installdir") is { Length: > 0 } dir ? Path.Combine(apps, "common", dir) : null;
                yield return new($"steam://rungameid/{id}", name, GameFiles.MainExe(folder, name), folder, "Steam");
            }
        }
    }
}

/// <summary>Epic Games: the launcher's install manifests (*.item, JSON). Games start through the launcher's link.</summary>
public sealed class EpicSource : IGameSource
{
    public string Id => "Epic";
    public static string ManifestFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public IEnumerable<GameEntry> Find(CancellationToken token) => FromManifests(ManifestFolder, token);

    public static IEnumerable<GameEntry> FromManifests(string folder, CancellationToken token = default)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*.item"))
        {
            token.ThrowIfCancellationRequested();
            GameEntry? entry = null;
            try { entry = Parse(File.ReadAllText(file)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { App.Log(ex); }
            if (entry != null) yield return entry;
        }
    }

    public static GameEntry? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string? Text(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        // Add-ons and engine installs share the folder; only games are listed.
        var isGame = root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array
            && categories.EnumerateArray().Any(category => string.Equals(category.GetString(), "games", StringComparison.OrdinalIgnoreCase));
        if (!isGame || Text("DisplayName") is not { Length: > 0 } name || Text("AppName") is not { Length: > 0 } app) return null;
        var folder = Text("InstallLocation");
        var exe = folder != null && Text("LaunchExecutable") is { Length: > 0 } launch ? Path.Combine(folder, launch) : null;
        var link = $"com.epicgames.launcher://apps/{Uri.EscapeDataString($"{Text("CatalogNamespace")}:{Text("CatalogItemId")}:{app}")}?action=launch&silent=true";
        return new(link, name.Trim('《', '》', ' '), exe, folder, "Epic");
    }
}

/// <summary>GOG Galaxy and offline installers: each game's registry key names its exe.</summary>
public sealed class GogSource : IGameSource
{
    public string Id => "GOG";

    public IEnumerable<GameEntry> Find(CancellationToken token)
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (games is null) yield break;
        foreach (var id in games.GetSubKeyNames())
        {
            token.ThrowIfCancellationRequested();
            using var game = games.OpenSubKey(id);
            if (game?.GetValue("gameName") is not string name || game.GetValue("exe") is not string exe || !File.Exists(exe)) continue;
            yield return new(exe, name, exe, game.GetValue("path") as string ?? Path.GetDirectoryName(exe), "GOG");
        }
    }
}

/// <summary>Ubisoft Connect: install folders by game id; games start through uplay://launch/.</summary>
public sealed class UbisoftSource : IGameSource
{
    public string Id => "Ubisoft";

    public IEnumerable<GameEntry> Find(CancellationToken token)
    {
        using var installs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (installs is null) yield break;
        foreach (var id in installs.GetSubKeyNames())
        {
            token.ThrowIfCancellationRequested();
            using var game = installs.OpenSubKey(id);
            if (game?.GetValue("InstallDir") is not string folder || !Directory.Exists(folder)) continue;
            var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
            yield return new($"uplay://launch/{id}/0", name, GameFiles.MainExe(folder, name), folder, "Ubisoft");
        }
    }
}

/// <summary>
/// Xbox app / Microsoft Store PC games: each drive's XboxGames folder holds one folder per game with a
/// Content\MicrosoftGame.config. Games start through their packaged app id, shell:AppsFolder\&lt;family&gt;!&lt;app&gt;.
/// </summary>
public sealed class XboxSource : IGameSource
{
    public string Id => "Xbox";

    public IEnumerable<GameEntry> Find(CancellationToken token)
    {
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed))
        {
            var library = Path.Combine(drive.Name, "XboxGames");
            if (!Directory.Exists(library)) continue;
            foreach (var folder in Directory.EnumerateDirectories(library))
            {
                token.ThrowIfCancellationRequested();
                var config = Path.Combine(folder, "Content", "MicrosoftGame.config");
                if (!File.Exists(config)) continue;
                GameEntry? entry = null;
                try { entry = Parse(File.ReadAllText(config), folder); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { App.Log(ex); }
                if (entry != null) yield return entry;
            }
        }
    }

    public static GameEntry? Parse(string xml, string folder)
    {
        var game = XDocument.Parse(xml).Root;
        var identity = game?.Element("Identity");
        var executable = game?.Element("ExecutableList")?.Elements("Executable").FirstOrDefault();
        if (identity?.Attribute("Name")?.Value is not { Length: > 0 } package || identity.Attribute("Publisher")?.Value is not { Length: > 0 } publisher
            || executable?.Attribute("Id")?.Value is not { Length: > 0 } app) return null;
        var display = game!.Element("ShellVisuals")?.Attribute("DefaultDisplayName")?.Value;
        var name = string.IsNullOrWhiteSpace(display) || display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(folder.TrimEnd('\\')) : display;
        var exe = executable.Attribute("Name")?.Value is { Length: > 0 } file ? Path.Combine(folder, "Content", file) : null;
        return new($@"shell:AppsFolder\{package}_{PublisherId(publisher)}!{app}", name, exe != null && File.Exists(exe) ? exe : null, Path.Combine(folder, "Content"), "Xbox");
    }

    /// <summary>
    /// The 13-character publisher id in a package family name: the first 8 bytes of SHA-256 over the publisher's
    /// UTF-16 name, read as 65 bits (zero-padded) in Crockford base32.
    /// </summary>
    public static string PublisherId(string publisher)
    {
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        var bits = string.Concat(hash.Take(8).Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))) + "0";
        const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        return string.Concat(Enumerable.Range(0, 13).Select(i => alphabet[Convert.ToInt32(bits.Substring(i * 5, 5), 2)]));
    }
}

/// <summary>
/// Games from launchers that keep no readable library of their own (EA app, Battle.net, Riot, miHoYo/HoYoverse,
/// WeGame…): Windows' list of installed programs, filtered to known game publishers or the WeGameApps folder. Only
/// entries whose icon or install folder names a runnable exe are listed, so the menu never starts the wrong file.
/// </summary>
public sealed class InstalledProgramsSource : IGameSource
{
    private static readonly Regex Publisher = new(@"Electronic Arts|Blizzard|Riot Games|miHoYo|HoYoverse|COGNOSPHERE|Hypergryph|鹰角|Kuro Games|库洛|NetEase Games|网易游戏|Bethesda|Rockstar Games|Square Enix|CD PROJEKT|BANDAI NAMCO|CAPCOM|SEGA|KONAMI|Paradox Interactive", RegexOptions.IgnoreCase);
    private static readonly Regex NotAGame = new(@"Battle\.net|EA app|^Origin\b|Riot Client|launcher|启动器|WeGame|redistributable|runtime|anti-?cheat|vanguard|uninstall|卸载|\bSDK\b|\btools?\b|editor|overlay|helper", RegexOptions.IgnoreCase);
    public string Id => "Installed";

    public IEnumerable<GameEntry> Find(CancellationToken token)
    {
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var id in uninstall.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();
                // Steam lists its games here too; the Steam source already has them.
                if (id.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)) continue;
                using var program = uninstall.OpenSubKey(id);
                if (program?.GetValue("DisplayName") is not string name || program.GetValue("SystemComponent") is 1 || program.GetValue("ParentKeyName") is not null || NotAGame.IsMatch(name)) continue;
                var uninstaller = program.GetValue("UninstallString") as string;
                var folder = program.GetValue("InstallLocation") as string is { Length: > 0 } location ? location.Trim('"') : GameFiles.ExeFromCommand(uninstaller) is { } stub ? Path.GetDirectoryName(stub) : null;
                var wegame = (folder ?? uninstaller ?? "").Contains(@"\WeGameApps\", StringComparison.OrdinalIgnoreCase);
                if (!wegame && !Publisher.IsMatch(program.GetValue("Publisher") as string ?? "")) continue;
                var icon = GameFiles.ExeFromCommand(program.GetValue("DisplayIcon") as string);
                var exe = icon != null && !GameLibraryService.IsSkippedExecutable(Path.GetFileNameWithoutExtension(icon)) ? icon : GameFiles.MainExe(folder, name, descend: false);
                if (exe is null) continue;
                yield return new(exe, name, exe, folder ?? Path.GetDirectoryName(exe), "Installed");
            }
        }
    }
}

/// <summary>Folders the user listed, scanned for executables that sit next to a recognisable game engine.</summary>
public sealed class FolderSource(IReadOnlyList<string> folders) : IGameSource
{
    public string Id => "Folders";

    public IEnumerable<GameEntry> Find(CancellationToken token) =>
        GameLibraryService.Scan(folders, token).Select(exe => new GameEntry(exe, Path.GetFileNameWithoutExtension(exe), exe, Path.GetDirectoryName(exe), "Folders"));
}
