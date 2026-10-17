using System.IO;
using System.Runtime.InteropServices;
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
        object? shell=null;
        try {
            var file=Path.Combine(folder,"有 空格.txt"); File.WriteAllText(file,"");
            Assert.Equal(file,FileLocationService.ResolveTarget(file));
            Assert.Equal(folder,FileLocationService.ResolveTarget(folder));
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            void Link(string path,string target) { object link=((dynamic)shell!).CreateShortcut(path); try { ((dynamic)link).TargetPath=target; ((dynamic)link).Save(); } finally { Marshal.ReleaseComObject(link); } }
            var first=Path.Combine(folder,"第一.lnk"); var second=Path.Combine(folder,"第二.lnk");
            Link(first,file); Link(second,first);
            Assert.Equal(file,FileLocationService.ResolveTarget(second));
            Link(first,Path.Combine(folder,"missing.exe")); Assert.Null(FileLocationService.ResolveTarget(first));
            var url=Path.Combine(folder,"local.url"); File.WriteAllText(url,"[InternetShortcut]\nURL="+new Uri(file).AbsoluteUri);
            Assert.Equal(file,FileLocationService.ResolveTarget(url));
            File.WriteAllText(url,"[InternetShortcut]\nURL=https://example.com"); Assert.Null(FileLocationService.ResolveTarget(url));
            Assert.Null(FileLocationService.ResolveTarget(Path.Combine(folder,"missing.exe")));
            Assert.Null(FileLocationService.ResolveTarget("shell:MyComputerFolder"));
        } finally { if(shell!=null) Marshal.ReleaseComObject(shell); Directory.Delete(folder,true); }
    }
}
