using System.IO;
using QuickMenu.Models;
using QuickMenu.Services;
using QuickMenu.Services.GameSources;
using Xunit;
namespace QuickMenu.Tests;

public class GameSourcesTests
{
    [Fact]
    public void VdfReadsNestedBlocksEscapesAndComments()
    {
        var root = Vdf.Parse("// comment\n\"libraryfolders\"\n{\n \"0\" { \"path\" \"C:\\\\Steam\" \"apps\" { \"10\" \"1\" } }\n \"1\" { \"Path\" \"E:\\\\Steam Library\" }\n}");
        var folders = root.Child("libraryfolders")!;
        Assert.Equal(@"C:\Steam", folders.Child("0")!.Text("path"));
        Assert.Equal(@"E:\Steam Library", folders.Child("1")!.Text("path"));
        Assert.Equal("1", folders.Child("0")!.Child("apps")!.Text("10"));
    }

    [Fact]
    public void SteamListsInstalledGamesFromEveryLibraryAndSkipsToolsSoundtracksAndDownloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuickMenuSteam-" + Guid.NewGuid());
        var second = Path.Combine(root, "Library2");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            Directory.CreateDirectory(Path.Combine(second, "steamapps", "common", "Hollow Game"));
            File.WriteAllText(Path.Combine(second, "steamapps", "common", "Hollow Game", "HollowGame.exe"), "");
            File.WriteAllText(Path.Combine(second, "steamapps", "common", "Hollow Game", "UnityCrashHandler64.exe"), "");
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{root.Replace("\\", "\\\\")}\" }} \"1\" {{ \"path\" \"{second.Replace("\\", "\\\\")}\" }} }}");
            void Manifest(string library, string id, string name, int flags) =>
                File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{id}.acf"), $"\"AppState\" {{ \"appid\" \"{id}\" \"name\" \"{name}\" \"StateFlags\" \"{flags}\" \"installdir\" \"{name}\" }}");
            Manifest(second, "100", "Hollow Game", 4);
            Manifest(root, "228980", "Steamworks Common Redistributables", 4);
            Manifest(root, "200", "Hollow Game Soundtrack", 4);
            Manifest(root, "300", "Still Downloading", 1026);
            var games = SteamSource.FromRoot(root).ToArray();
            var game = Assert.Single(games);
            Assert.Equal("steam://rungameid/100", game.Launch);
            Assert.Equal("Hollow Game", game.Name);
            Assert.Equal(Path.Combine(second, "steamapps", "common", "Hollow Game", "HollowGame.exe"), game.Exe);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void EpicManifestBecomesLauncherLinkAndAddOnsAreSkipped()
    {
        var game = EpicSource.Parse("""{ "DisplayName": "《Trine 4》", "AppName": "app1", "CatalogNamespace": "ns", "CatalogItemId": "item", "InstallLocation": "C:\\Games\\Trine4", "LaunchExecutable": "trine4.exe", "AppCategories": ["public", "games", "applications"] }""");
        Assert.NotNull(game);
        Assert.Equal("Trine 4", game!.Name);
        Assert.Equal("com.epicgames.launcher://apps/ns%3Aitem%3Aapp1?action=launch&silent=true", game.Launch);
        Assert.Equal(@"C:\Games\Trine4\trine4.exe", game.Exe);
        Assert.Null(EpicSource.Parse("""{ "DisplayName": "Expansion", "AppName": "dlc", "AppCategories": ["addons"] }"""));
    }

    [Fact]
    public void XboxConfigBecomesPackagedAppLink()
    {
        Assert.Equal("8wekyb3d8bbwe", XboxSource.PublisherId("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"));
        var game = XboxSource.Parse("""
            <Game configVersion="1">
              <Identity Name="Microsoft.Example" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.0.0.0"/>
              <ExecutableList><Executable Name="gamelaunchhelper.exe" Id="Game"/></ExecutableList>
              <ShellVisuals DefaultDisplayName="Example Game"/>
            </Game>
            """, @"D:\XboxGames\Example Game");
        Assert.Equal(@"shell:AppsFolder\Microsoft.Example_8wekyb3d8bbwe!Game", game!.Launch);
        Assert.Equal("Example Game", game.Name);
    }

    [Fact]
    public void MergeListsEachGameOnceAddsHandPickedGamesAndLeavesHiddenOnesOut()
    {
        var settings = new GameLibrarySettings { Added = [@"C:\Mine\Solo.exe"], Hidden = ["steam://rungameid/2"] };
        var (games, counts) = GameLibraryService.Merge(
        [
            new("steam://rungameid/1", "One", @"D:\Steam\One\One.exe", @"D:\Steam\One", "Steam"),
            new(@"D:\Steam\One\One.exe", "One", @"D:\Steam\One\One.exe", @"D:\Steam\One", "Folders"),
            new("steam://rungameid/2", "Two", null, null, "Steam")
        ], settings);
        Assert.Equal(["One", "Solo"], games.Select(game => game.Name));
        Assert.Equal(2, counts["Steam"]);
        Assert.Equal(1, counts["Added"]);
        Assert.False(counts.ContainsKey("Folders"));
    }

    [Fact]
    public void LibrarySettingsMigrateTheOldFolderListAndRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuickMenuLibrary-" + Guid.NewGuid() + ".json");
        try
        {
            var service = new GameLibraryService(path);
            Assert.Empty(service.Load().Folders);
            Assert.True(service.Load().IsEnabled("Steam"));
            File.WriteAllText(path, "[\"D:\\\\Games\", \"E:\\\\More\"]");
            Assert.Equal([@"D:\Games", @"E:\More"], service.Load().Folders);
            var settings = service.Load();
            settings.Sources["Xbox"] = false; settings.Hidden.Add("steam://rungameid/9");
            Assert.True(service.Save(settings).Success);
            var loaded = service.Load();
            Assert.False(loaded.IsEnabled("xbox"));
            Assert.Equal([@"D:\Games", @"E:\More"], loaded.Folders);
            Assert.Equal(["steam://rungameid/9"], loaded.Hidden);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ScanFindsGodotGameMakerAndRpgMakerGames()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuickMenuEngines-" + Guid.NewGuid());
        try
        {
            void Game(string folder, string exe, params string[] files)
            {
                var directory = Path.Combine(root, folder); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, exe), "");
                foreach (var file in files) { if (file.EndsWith('/')) Directory.CreateDirectory(Path.Combine(directory, file.TrimEnd('/'))); else File.WriteAllText(Path.Combine(directory, file), ""); }
            }
            Game("Godot", "Hero.exe", "Hero.pck");
            Game("GameMaker", "Runner.exe", "data.win");
            Game("RpgMaker", "Game.exe", "www/");
            Game("Plain", "Tool.exe");
            Assert.Equal(3, GameLibraryService.Scan([root]).Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("steam://rungameid/570", true)]
    [InlineData("com.epicgames.launcher://apps/ns%3Aid%3AFortnite?action=launch&silent=true", true)]
    [InlineData("uplay://launch/635/0", true)]
    [InlineData(@"shell:AppsFolder\Microsoft.Game_8wekyb3d8bbwe!Game", true)]
    [InlineData("ms-settings:", false)]
    [InlineData("https://example.com/game", false)]
    [InlineData("steam://install/570", false)]
    [InlineData("calc", false)]
    public void OnlyStoreLinksStartAsGames(string link, bool started) => Assert.Equal(started, DesktopService.IsStoreLink(link));
}
