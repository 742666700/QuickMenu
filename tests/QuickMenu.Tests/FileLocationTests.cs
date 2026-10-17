using System.IO;
using QuickMenu.Interop;
using QuickMenu.Services;
using Xunit;
namespace QuickMenu.Tests;
public class FileLocationTests
{
    [Fact]
    public void ResolvesFilesFoldersShortcutChainsAndLocalUrlsWithoutLaunchingAnything()
    {
        var folder=Path.Combine(Path.GetTempPath(),"QuickMenuLocation-"+Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try {
            var file=Path.Combine(folder,"有 空格.txt"); File.WriteAllText(file,"");
            Assert.Equal(file,FileLocationService.ResolveTarget(file));
            Assert.Equal(folder,FileLocationService.ResolveTarget(folder));
            void Link(string path,string target) => ShellLink.Create(path,target);
            var first=Path.Combine(folder,"第一.lnk"); var second=Path.Combine(folder,"第二.lnk");
            Link(first,file); Link(second,first);
            Assert.Equal(file,FileLocationService.ResolveTarget(second));
            Link(first,Path.Combine(folder,"missing.exe")); Assert.Null(FileLocationService.ResolveTarget(first));
            var url=Path.Combine(folder,"local.url"); File.WriteAllText(url,"[InternetShortcut]\nURL="+new Uri(file).AbsoluteUri);
            Assert.Equal(file,FileLocationService.ResolveTarget(url));
            File.WriteAllText(url,"[InternetShortcut]\nURL=https://example.com"); Assert.Null(FileLocationService.ResolveTarget(url));
            Assert.Null(FileLocationService.ResolveTarget(Path.Combine(folder,"missing.exe")));
            Assert.Null(FileLocationService.ResolveTarget("shell:MyComputerFolder"));
        } finally { Directory.Delete(folder,true); }
    }
}
