using System.IO;
using QuickMenu.Models;
using QuickMenu.Services;
using Xunit;
namespace QuickMenu.Tests;

// The automatic rules are static; these tests restore the defaults when they finish.
[Collection("CategoryRules")]
public class CategoryRulesTests
{
    [Fact]
    public void BuiltInRulesCanBeChangedAndRestored()
    {
        try
        {
            Assert.True(DesktopCategories.IsAiTool(@"C:\Users\a\Desktop\Cherry-Studio.lnk", "Cherry-Studio"));
            Assert.False(DesktopCategories.IsAiTool(@"C:\Users\a\Desktop\Traefik.lnk", "Traefik"));
            Assert.Equal(DesktopCategory.Games, DesktopCategories.Classify(@"C:\Users\a\Desktop\Game.lnk", @"D:\Games\Game\Game.exe"));
            DesktopCategories.Configure(new CategorySettings
            {
                AiKeywords = ["Notepad", "通义"], GameFolders = ["Emulators"],
                Extensions = new() { ["Documents"] = ["pdf", ".epub"] }
            });
            Assert.True(DesktopCategories.IsAiTool(@"C:\x\Notepad++.lnk", "Notepad++"));
            Assert.True(DesktopCategories.IsAiTool(@"C:\x\通义灵码.lnk", "通义灵码"));
            Assert.False(DesktopCategories.IsAiTool(@"C:\x\ChatGPT.lnk", "ChatGPT"));
            Assert.Equal(DesktopCategory.Games, DesktopCategories.Classify(@"C:\x\Yuzu.lnk", @"E:\Emulators\yuzu\yuzu.exe"));
            Assert.Equal(DesktopCategory.Programs, DesktopCategories.Classify(@"C:\x\Game.lnk", @"D:\Games\Game\Game.exe"));
            Assert.Equal(DesktopCategory.Documents, DesktopCategories.Classify(@"C:\x\book.epub"));
            Assert.Equal(DesktopCategory.Other, DesktopCategories.Classify(@"C:\x\notes.txt"));
            Assert.Equal(DesktopCategory.Spreadsheets, DesktopCategories.Classify(@"C:\x\sheet.xlsx"));
        }
        finally { DesktopCategories.Configure(null); }
        Assert.Equal(DesktopCategory.Documents, DesktopCategories.Classify(@"C:\x\notes.txt"));
    }

    [Fact]
    public void CustomCategoryMatchesByNameExtensionOrPath()
    {
        var category = new CustomCategory { Name = "工作", Keywords = ["VPN"], Extensions = ["psd"], PathContains = [@"D:/Work"] };
        Assert.True(category.Matches("Company VPN", @"C:\Desktop\Company VPN.lnk", null));
        Assert.True(category.Matches("Poster", @"C:\Desktop\Poster.lnk", @"E:\Art\poster.PSD"));
        Assert.True(category.Matches("Tracker", @"C:\Desktop\Tracker.lnk", @"D:\Work\tracker.exe"));
        Assert.False(category.Matches("Music", @"C:\Desktop\Music.lnk", @"E:\Music\player.exe"));
        Assert.False(new CustomCategory { Name = "空" }.HasRules);
    }

    [Fact]
    public void PreferencesKeepOneHandPickedCategoryAndTheAiMark()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuickMenuPrefs-" + Guid.NewGuid() + ".json");
        try
        {
            var preferences = new DesktopPreferencesService(path);
            Assert.True(preferences.SetCustom("a", "work").Success);
            Assert.True(preferences.SetAiTool("a", true).Success);
            Assert.Equal("work", preferences.Get("a").Custom);
            preferences.SetCategory("a", DesktopCategory.Games);
            Assert.Null(preferences.Get("a").Custom);
            Assert.True(preferences.Get("a").AiTool);
            preferences.SetCustom("a", "work");
            Assert.Null(preferences.Get("a").Category);
            var reloaded = new DesktopPreferencesService(path);
            Assert.Equal("work", reloaded.Get("a").Custom);
            Assert.True(reloaded.Get("a").AiTool);
            reloaded.SetCategory("a", null);
            Assert.Null(reloaded.Get("a").Custom);
            Assert.Null(reloaded.Get("a").AiTool);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CategorySettingsRoundTripAndSplitTypedLists()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuickMenuCategories-" + Guid.NewGuid() + ".json");
        try
        {
            var service = new CategoryService(path);
            Assert.Empty(service.Load().Custom);
            var settings = new CategorySettings { ShowGamesTab = false, Custom = [new CustomCategory { Name = "工作", Keywords = ["VPN"] }, new CustomCategory { Name = "" }] };
            Assert.True(service.Save(settings).Success);
            var loaded = service.Load();
            Assert.False(loaded.ShowGamesTab);
            Assert.Equal("工作", Assert.Single(loaded.Custom).Name);
            Assert.Null(loaded.AiKeywords);
        }
        finally { File.Delete(path); }
        Assert.Equal(["Cherry Studio", "豆包", "Kimi"], CategoryService.SplitList("Cherry Studio，豆包、 Kimi\n"));
        Assert.Equal(["pdf", ".psd", "ai"], CategoryService.SplitList("pdf .psd, ai", spacesSeparate: true));
    }
}
