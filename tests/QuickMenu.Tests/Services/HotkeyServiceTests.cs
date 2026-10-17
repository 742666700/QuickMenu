using System.Windows.Interop;
using QuickMenu.Services;
using Xunit;

namespace QuickMenu.Tests.Services;

// Needs a signed-in Windows desktop; CI skips it (dotnet test --filter Category!=Interactive).
[Trait("Category", "Interactive")]
[Collection("原生窗口集成")]
public class HotkeyServiceTests
{
    [Fact]
    public async Task ConflictingReplacementPreservesTheWorkingBindingAndDisposeReleasesIt()
    {
        var completion = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                using var firstWindow = new HwndSource(new HwndSourceParameters("QuickMenu 热键测试一") { Width = 1, Height = 1 });
                using var secondWindow = new HwndSource(new HwndSourceParameters("QuickMenu 热键测试二") { Width = 1, Height = 1 });
                using var thirdWindow = new HwndSource(new HwndSourceParameters("QuickMenu 热键测试三") { Width = 1, Height = 1 });
                using var first = new HotkeyService(firstWindow.Handle);
                using var second = new HotkeyService(secondWindow.Handle);
                using var third = new HotkeyService(thirdWindow.Handle);
                const uint modifiers = 7; // Alt + Ctrl + Shift
                Assert.True(first.TryRegister(modifiers, 0x87).Success); // F24
                Assert.True(second.TryRegister(modifiers, 0x86).Success); // F23
                Assert.False(first.TryRegister(modifiers, 0x86).Success);
                Assert.False(third.TryRegister(modifiers, 0x87).Success);
                first.Dispose();
                Assert.True(third.TryRegister(modifiers, 0x87).Success);
                completion.SetResult(true);
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
