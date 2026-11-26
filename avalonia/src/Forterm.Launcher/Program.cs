using System.Diagnostics;
// xshell/xftp compatibility entry. Forwards argv as an array (never joined, lowercased or
// decoded) and waits for the main program, then returns its exit code. Controllers that watch
// the process they started must not see the entry exit before the main program has either
// forwarded the launch to a running instance or finished as the primary instance.
string entry=Path.GetFileNameWithoutExtension(Environment.ProcessPath??"xshell").ToLowerInvariant();
string executable=Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"Forterm.exe":"Forterm");
if(!File.Exists(executable))return Fail(2,"在这个文件夹里找不到 Forterm.exe：\n"+AppContext.BaseDirectory+"\n\n"+entry+".exe 不能单独使用，请保留整个 Forterm 文件夹，或重新安装。");
var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory};
info.ArgumentList.Add(entry=="xftp"?"--entry=xftp":"--entry=xshell");
foreach(string arg in args)info.ArgumentList.Add(arg);
try{using var process=Process.Start(info);if(process==null)return Fail(3,"Forterm.exe 没有启动。");process.WaitForExit();return process.ExitCode==4?Fail(4,"正在运行的 Forterm 没有接收这次打开。\n\n请关掉这个提示，再从控制器点一次；如果一直这样，请先关闭 Forterm。"):process.ExitCode;}
catch(Exception e){return Fail(3,"Forterm.exe 没有启动："+e.Message);}

// A controller shows nothing when the program it started ends at once, so the user is told why. Never the arguments:
// they hold a password. Not for --diagnose, nor where nobody could answer.
int Fail(int code,string text)
{
    if(OperatingSystem.IsWindows()&&Environment.UserInteractive&&!args.Any(a=>a.Equals("--diagnose",StringComparison.OrdinalIgnoreCase)))
    {
        // A controller may start the entry hidden, and Windows shows the first window of a program the way its starter
        // asked. A window of no size takes that, so the box is seen. Still it is waited for, but not for ever.
        TakeHiddenStart();
        var box=new Thread(()=>MessageBoxW(0,text,"Forterm",0x10|0x10000|0x40000)){IsBackground=true};box.Start();box.Join(TimeSpan.FromSeconds(60));
    }
    return code;
}
[System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern int MessageBoxW(nint owner,string text,string caption,uint type);

// Only when the starter asked for hidden or minimised (STARTF_USESHOWWINDOW with SW_HIDE, SW_SHOWMINIMIZED, SW_MINIMIZE,
// SW_SHOWMINNOACTIVE or SW_FORCEMINIMIZE): otherwise nothing is to be taken, and nothing is created.
static void TakeHiddenStart()
{
    var info=new StartupInfo{Size=System.Runtime.InteropServices.Marshal.SizeOf<StartupInfo>()};GetStartupInfoW(ref info);
    if((info.Flags&1)==0||info.ShowWindow is not (0 or 2 or 6 or 7 or 11))return;
    nint window=CreateWindowExW(0x80,"STATIC","",0x80000000,0,0,0,0,0,0,0,0);
    if(window==0)return;
    ShowWindow(window,1);DestroyWindow(window);
}
[System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern void GetStartupInfoW(ref StartupInfo info);
[System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]static extern nint CreateWindowExW(uint exStyle,string className,string title,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint param);
[System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindow(nint window,int show);
[System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool DestroyWindow(nint window);

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
struct StartupInfo{public int Size;public nint Reserved,Desktop,Title;public int X,Y,Width,Height,Columns,Rows,Fill,Flags;public short ShowWindow,Reserved2Size;public nint Reserved2,Input,Output,Error;}

