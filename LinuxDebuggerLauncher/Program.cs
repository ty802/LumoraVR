using System;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
namespace Program;

public class Native
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Waiting");

        Native.setsid();
        Native.kill(int.Parse(args[0]), 19);
        Console.WriteLine("The Game process has been stoped");
        string? execname = System.Environment.GetEnvironmentVariable("netcoredbg_path");
        string? terminal = System.Environment.GetEnvironmentVariable("debug_term");
        string? termargs = System.Environment.GetEnvironmentVariable("debug_term_args");
        if (execname is null)
            goto END;
        System.IO.FileInfo file = new System.IO.FileInfo(execname);
        if (!file.Exists)
            goto END;
        ProcessStartInfo procsargs;
        if (terminal is not null || termargs is not null)
        {
            if (terminal is null) { Console.WriteLine("Terminal is required for termargs"); goto END; }
            List<string> nargs = new();
            if(termargs is not null)
                nargs.AddRange(termargs.Split(" "));
            nargs.Add($"{execname} --attach {args[0]}");
            procsargs = new ProcessStartInfo(terminal,nargs.ToArray());
        }else{
            procsargs = new ProcessStartInfo(execname,$"--attach {args[0]}");
        }
        Console.WriteLine("Starting Debugger");
        Process.Start(procsargs)?.Dispose();
    END:
        Console.WriteLine("Resumeing the Game Process");
        System.Threading.Thread.Sleep(600);
        Native.kill(int.Parse(args[0]), 18);

    }

    [DllImport("libc", SetLastError = true)]
    public static extern int kill(int pid, int sig);
    [DllImport("libc", SetLastError = true)]
    public static extern int setsid();

}

