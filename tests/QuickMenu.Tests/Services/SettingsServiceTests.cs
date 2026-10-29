using System.IO;
using QuickMenu.Models;
using QuickMenu.Services;
using Xunit;

namespace QuickMenu.Tests.Services;

public class SettingsServiceTests
{
    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"CharacterMotion\":false}", false)]
    [InlineData("{\"CharacterMotion\":true}", true)]
    public void CharacterMotionDefaultsOnAndRoundTrips(string json, bool expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var service = new SettingsService(path, false);
            var settings = service.Load();
            Assert.Equal(expected, settings.CharacterMotion);
            Assert.True(service.Save(settings).Success);
            Assert.Equal(expected, service.Load().CharacterMotion);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("12")]
    [InlineData("\"off\"")]
    [InlineData("{}")]
    public void InvalidCharacterMotionResetsOnlyThatField(string invalid)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Version\":2,\"Character\":\"gemini\",\"StayOpenOnClickOutside\":true,\"CharacterMotion\":" + invalid + "}");
            var loaded = new SettingsService(path, false).Load();
            Assert.True(loaded.CharacterMotion);
            Assert.Equal("gemini", loaded.Character);
            Assert.True(loaded.StayOpenOnClickOutside);
            Assert.Single(SetAside(path));
        }
        finally { File.Delete(path); foreach (var copy in SetAside(path)) File.Delete(copy); }
    }
    [Fact]
    public void MalformedSettingsFallBackWithoutThrowing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{ invalid");
            var settings = new SettingsService(path, false).Load();
            Assert.Equal(1u, settings.HotkeyModifiers);
            Assert.Equal(32u, settings.HotkeyKey);
            Assert.False(settings.StartWithWindows);
            // The unreadable file is kept beside it, since the next save replaces it.
            Assert.Equal("{ invalid", File.ReadAllText(Assert.Single(SetAside(path))));
        }
        finally { File.Delete(path); foreach (var copy in SetAside(path)) File.Delete(copy); }
    }

    [Fact]
    public void AnInvalidFieldIsResetWithoutLosingTheOthers()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"HotkeyKey\":0,\"HotkeyModifiers\":2,\"Theme\":7,\"Character\":\"gemini\",\"StayOpenOnClickOutside\":true}");
            var loaded = new SettingsService(path, false).Load();
            Assert.Equal((1u, 32u), (loaded.HotkeyModifiers, loaded.HotkeyKey));
            Assert.Equal(ThemeMode.DeepSeek, loaded.Theme);
            Assert.Equal("gemini", loaded.Character);
            Assert.True(loaded.StayOpenOnClickOutside);
            Assert.Single(SetAside(path));
        }
        finally { File.Delete(path); foreach (var copy in SetAside(path)) File.Delete(copy); }
    }

    private static string[] SetAside(string path) =>
        Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".broken-*");

    [Theory]
    [InlineData(ThemeMode.ChatGpt)]
    [InlineData(ThemeMode.DeepSeek)]
    [InlineData(ThemeMode.Gemini)]
    [InlineData(ThemeMode.Cream)]
    [InlineData(ThemeMode.Oled)]
    [InlineData(ThemeMode.OledDay)]
    public void SettingsRoundTripPreservesExplicitChoices(ThemeMode theme)
    {
        var path = Path.Combine(Path.GetTempPath(), "QuickMenu-test-" + Guid.NewGuid() + ".json");
        try
        {
            var service = new SettingsService(path, false);
            Assert.True(service.Save(new() { Theme = theme, HotkeyModifiers = 6, HotkeyKey = 75 }).Success);
            var loaded = service.Load();
            Assert.Equal(theme, loaded.Theme);
            Assert.Equal(6u, loaded.HotkeyModifiers);
            Assert.Equal(75u, loaded.HotkeyKey);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1, ThemeMode.OledDay)]
    [InlineData(2, ThemeMode.Oled)]
    public void RetiredLightAndDarkThemesMoveToOled(int saved, ThemeMode expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Theme\":" + saved + "}");
            Assert.Equal(expected, new SettingsService(path, false).Load().Theme);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"Character\":\"gemini\"}", "gemini")]
    [InlineData("{}", "claude")]
    [InlineData("{\"ShowCompanion\":true}", "claude")]
    [InlineData("{\"ShowCompanion\":false}", "")]
    [InlineData("{\"Character\":\"\"}", "")]
    public void CharacterChoiceSurvivesTheOldOnOffSwitch(string json, string expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var service = new SettingsService(path, false);
            var loaded = service.Load();
            Assert.Equal(expected, loaded.Character);
            Assert.True(service.Save(loaded).Success);
            Assert.DoesNotContain("ShowCompanion", File.ReadAllText(path));
            Assert.Equal(expected, service.Load().Character);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}", true, true)]
    [InlineData("{\"CharacterFollowsTheme\":false,\"ShowWorldClock\":false}", false, false)]
    public void FollowThemeAndWorldClockDefaultOnAndPersist(string json, bool follows, bool clock)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var service = new SettingsService(path, false);
            var loaded = service.Load();
            Assert.Equal(follows, loaded.CharacterFollowsTheme);
            Assert.Equal(clock, loaded.ShowWorldClock);
            Assert.True(service.Save(loaded).Success);
            Assert.Equal(clock, service.Load().ShowWorldClock);
        }
        finally { File.Delete(path); }
    }

    // Older settings files have no such field: the search box keeps the cursor, as before.
    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"FocusSearchOnOpen\":false}", false)]
    public void SearchBoxFocusDefaultsOnAndPersists(string json, bool focus)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var service = new SettingsService(path, false);
            var loaded = service.Load();
            Assert.Equal(focus, loaded.FocusSearchOnOpen);
            Assert.True(service.Save(loaded).Success);
            Assert.Equal(focus, service.Load().FocusSearchOnOpen);
        }
        finally { File.Delete(path); }
    }

    // Older settings files have no such field: clicking outside still hides the menu, as before.
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"StayOpenOnClickOutside\":true}", true)]
    public void StayOpenDefaultsOffAndPersists(string json, bool stayOpen)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var service = new SettingsService(path, false);
            var loaded = service.Load();
            Assert.Equal(stayOpen, loaded.StayOpenOnClickOutside);
            Assert.True(service.Save(loaded).Success);
            Assert.Equal(stayOpen, service.Load().StayOpenOnClickOutside);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"HotkeyKey\":0}")]
    [InlineData("{\"Theme\":99}")]
    public void InvalidParsedSettingsUseSafeDefaults(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            var loaded = new SettingsService(path, false).Load();
            Assert.Equal(ThemeMode.System, loaded.Theme);
            Assert.Equal(32u, loaded.HotkeyKey);
        }
        finally { File.Delete(path); foreach (var copy in SetAside(path)) File.Delete(copy); }
    }

    [Fact]
    public void OneXSettingsKeepTheirClockAndSkipTheWelcomeWhileNewInstallsStartPlain()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuickMenuSettings-" + Guid.NewGuid() + ".json");
        try
        {
            var fresh = new SettingsService(path, false).Load();
            Assert.False(fresh.WelcomeShown);
            Assert.Equal("", fresh.ClockLabel);
            Assert.Null(fresh.SecondClockZone);
            Assert.Equal(AppSettings.CurrentVersion, fresh.Version);

            File.WriteAllText(path, """{ "HotkeyModifiers": 1, "HotkeyKey": 32, "Theme": 8, "ShowWorldClock": true }""");
            var migrated = new SettingsService(path, false).Load();
            Assert.True(migrated.WelcomeShown);
            Assert.Equal("美国时间", migrated.ClockLabel);
            Assert.Equal("China Standard Time", migrated.SecondClockZone);
            Assert.Equal("北京时间", migrated.SecondClockLabel);

            migrated.SecondClockZone = null; migrated.ClockLabel = "";
            Assert.True(new SettingsService(path, false).Save(migrated).Success);
            var reloaded = new SettingsService(path, false).Load();
            Assert.Null(reloaded.SecondClockZone);
            Assert.Equal("", reloaded.ClockLabel);
            Assert.True(reloaded.WelcomeShown);
        }
        finally { File.Delete(path); }
    }
}
