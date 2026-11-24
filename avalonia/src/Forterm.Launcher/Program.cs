using System.Diagnostics;
// xshell/xftp compatibility entry. Forwards argv as an array (never joined, lowercased or
// decoded) and waits for the main program, then returns its exit code. Controllers that watch
// the process they started must not see the entry exit before the main program has either
// forwarded the launch to a running instance or finished as the primary instance.
string entry=Path.GetFileNameWithoutExtension(Environment.ProcessPath??"xshell").ToLowerInvariant();
string executable=Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"Forterm.exe":"Forterm");
if(!File.Exists(executable))return 2;
var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=AppContext.BaseDirectory};
info.ArgumentList.Add(entry=="xftp"?"--entry=xftp":"--entry=xshell");
foreach(string arg in args)info.ArgumentList.Add(arg);
try{using var process=Process.Start(info);if(process==null)return 3;process.WaitForExit();return process.ExitCode;}catch{return 3;}
