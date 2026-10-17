using QuickMenu.Models;
using Xunit;

namespace QuickMenu.Tests;

// Shares the static rules with CategoryRulesTests, so the two never run at the same time.
[Collection("CategoryRules")]
public class DesktopCategoryTests
{
    [Theory]
    [InlineData("游戏.url", "steam://rungameid/123", DesktopCategory.Games)]
    [InlineData("游戏.url", "com.epicgames.launcher://apps/test?action=launch", DesktopCategory.Games)]
    [InlineData("游戏.lnk", @"D:\Games\Example\game.exe", DesktopCategory.Games)]
    [InlineData("游戏.lnk", @"D:\SteamLibrary\steamapps\common\Example\game.exe", DesktopCategory.Games)]
    [InlineData("游戏.lnk", @"C:\WeGameApps\Example\game.exe", DesktopCategory.Games)]
    [InlineData("文档.lnk", @"D:\Games\readme.pdf", DesktopCategory.Documents)]
    [InlineData("普通.lnk", @"C:\Tools\tool.exe", DesktopCategory.Programs)]
    [InlineData("网页.url", "https://example.com/games/", DesktopCategory.Programs)]
    public void RecognizesGameTargets(string path, string target, DesktopCategory expected)
        => Assert.Equal(expected, DesktopCategories.Classify(path, target));

    [Fact]
    public void GameFilterIncludesOnlyGames()
    {
        Assert.True(DesktopCategories.Matches(DesktopCategory.Games, DesktopCategory.Games));
        Assert.True(DesktopCategories.Matches(DesktopCategory.All, DesktopCategory.Games));
        Assert.False(DesktopCategories.Matches(DesktopCategory.Games, DesktopCategory.Programs));
        Assert.False(DesktopCategories.Matches(DesktopCategory.Office, DesktopCategory.Games));
    }

    [Theory]
    [InlineData("Grok Build.lnk", "Grok Build", true)]
    [InlineData("DeepSeek Harness.lnk", "DeepSeek Harness", true)]
    [InlineData("豆包.lnk", "豆包", true)]
    [InlineData("Qoder CN.lnk", "Qoder CN", true)]
    [InlineData("ChatGPT.lnk", "ChatGPT", true)]
    [InlineData("Claude.lnk", "Claude", true)]
    [InlineData("OpenCode.lnk", "OpenCode", true)]
    [InlineData("Trae CN.lnk", "Trae CN", true)]
    [InlineData("元宝.lnk", "元宝", true)]
    [InlineData("Kimi.lnk", "Kimi", true)]
    [InlineData("Codex Guardian.lnk", "Codex Guardian", true)]
    [InlineData("AI需求.docx", "AI需求", false)]
    [InlineData("Grok使用说明.pdf", "Grok使用说明", false)]
    [InlineData("普通.lnk", "普通", false)]
    public void AiToolsOnlyIncludeRecognizedLaunchers(string path, string name, bool expected) => Assert.Equal(expected, DesktopCategories.IsAiTool(path, name));
    [Theory]
    [InlineData("AI工具", true)]
    [InlineData("AI 工具", true)]
    [InlineData("ai tools", true)]
    [InlineData("AI", true)]
    [InlineData("AI需求", false)]
    [InlineData("工具", false)]
    public void AiFolderNamesAreRecognized(string name, bool expected) => Assert.Equal(expected, DesktopCategories.IsAiFolder(name));
    [Fact]
    public void AiToolTargetIdentifiesRenamedShortcut() => Assert.True(DesktopCategories.IsAiTool("助手.lnk", "助手", @"C:\apps\Claude.exe"));
    [Fact]
    public void OfficeIncludesAllThreeTypesAndAiStillAppearsInPrograms()
    {
        Assert.True(DesktopCategories.Matches(DesktopCategory.Office, DesktopCategory.Documents));
        Assert.True(DesktopCategories.Matches(DesktopCategory.Office, DesktopCategory.Spreadsheets));
        Assert.True(DesktopCategories.Matches(DesktopCategory.Office, DesktopCategory.Presentations));
        Assert.False(DesktopCategories.Matches(DesktopCategory.Office, DesktopCategory.Media));
        Assert.True(DesktopCategories.Matches(DesktopCategory.AiTools, DesktopCategory.Programs, true));
        Assert.True(DesktopCategories.Matches(DesktopCategory.Programs, DesktopCategory.Programs, true));
    }
    [Theory]
    [InlineData("报告.DOCX", DesktopCategory.Documents)]
    [InlineData("参考.pdf", DesktopCategory.Documents)]
    [InlineData("数据.xlsx", DesktopCategory.Spreadsheets)]
    [InlineData("数据.CSV", DesktopCategory.Spreadsheets)]
    [InlineData("汇报.pptx", DesktopCategory.Presentations)]
    [InlineData("照片.JPG", DesktopCategory.Media)]
    [InlineData("录像.mp4", DesktopCategory.Media)]
    [InlineData("应用.lnk", DesktopCategory.Programs)]
    [InlineData("网站.url", DesktopCategory.Programs)]
    [InlineData("应用.exe", DesktopCategory.Programs)]
    [InlineData("shell:MyComputerFolder", DesktopCategory.Other)]
    [InlineData("未知.zip", DesktopCategory.Other)]
    public void ClassifiesCaseInsensitiveExtensions(string path, DesktopCategory expected) => Assert.Equal(expected, DesktopCategories.Classify(path));

    [Fact]
    public void DocumentShortcutUsesTargetCategory() => Assert.Equal(DesktopCategory.Spreadsheets, DesktopCategories.Classify("账单.lnk", @"C:\文档\账单.xlsx"));
    [Fact]
    public void DirectoryNamedLikeDocumentIsNotAFile() => Assert.Equal(DesktopCategory.Other, DesktopCategories.Classify("文件夹.pdf", isFolder:true));
    [Fact]
    public void CategorySurvivesPageSwitchButResetsOnReinvoke()
    {
        var session = new MenuSession(); session.Begin(); session.DesktopCategory = DesktopCategory.Media;
        session.Cycle(); session.Cycle(); Assert.Equal(DesktopCategory.Media, session.DesktopCategory);
        session.Begin(); Assert.Equal(DesktopCategory.All, session.DesktopCategory);
    }
}
