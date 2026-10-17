using System.IO;
using QuickMenu.Services;
using Xunit;
namespace QuickMenu.Tests;
public class GameLibraryTests
{
    [Fact]
    public void ScanFiltersToolsBackupsAndDuplicateRootsAndSeesNewGames()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuickMenuScan-" + Guid.NewGuid());
        try {
            var game = Path.Combine(root, "Example"); Directory.CreateDirectory(Path.Combine(game, "Example_Data"));
            File.WriteAllText(Path.Combine(game, "Example.exe"), "");
            File.WriteAllText(Path.Combine(game, "UnityCrashHandler64.exe"), "");
            File.WriteAllText(Path.Combine(root, "installer.exe"), "");
            var backup = Path.Combine(root, "backup"); Directory.CreateDirectory(backup);
            File.WriteAllText(Path.Combine(backup, "steam_api64.dll"), ""); File.WriteAllText(Path.Combine(backup, "Old.exe"), "");
            var first = GameLibraryService.Scan(new[] { root, root, Path.Combine(root, "missing") });
            Assert.Equal(new[] { Path.Combine(game, "Example.exe") }, first);
            var secondGame = Path.Combine(root, "NewGame"); Directory.CreateDirectory(Path.Combine(secondGame, "game")); Directory.CreateDirectory(Path.Combine(secondGame, "renpy"));
            File.WriteAllText(Path.Combine(secondGame, "NewGame.exe"), ""); File.WriteAllText(Path.Combine(secondGame, "NewGame-32.exe"), "");
            Assert.Equal(2, GameLibraryService.Scan(new[] { root }).Count);
            File.Delete(Path.Combine(game, "Example.exe"));
            Assert.Single(GameLibraryService.Scan(new[] { root }));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => GameLibraryService.Scan(new[] { root }, cancellation.Token));
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
