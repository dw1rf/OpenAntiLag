using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenAntiLag {
    // Own only this invocation and its children. Closing the app cannot leave a warmer behind.
    sealed class ShaderJob : IDisposable {
        [StructLayout(LayoutKind.Sequential)] struct BasicLimits {
            public long ProcessTime,JobTime;public uint Flags;public UIntPtr Minimum,Maximum;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)] struct Limits {
            public BasicLimits Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int type,ref Limits data,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        IntPtr handle;
        public ShaderJob() {
            handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits=new Limits();limits.Basic.Flags=0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if(!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits)))){int error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error);}
        }
        public void Attach(Process process) {if(!AssignProcessToJobObject(handle,process.Handle))throw new Win32Exception(Marshal.GetLastWin32Error(),"Не удалось изолировать процесс подготовки шейдеров.");}
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
    public static class ShaderProcess {
        public static Task<string> Run(string executable,string arguments,string game,Action<string> log,CancellationToken ct) {
            return Task.Run(delegate {
                ct.ThrowIfCancellationRequested();
                using(var job=new ShaderJob())
                using(var process=new Process()) {
                    process.StartInfo=new ProcessStartInfo(executable,arguments) {
                        WorkingDirectory=Path.GetDirectoryName(executable),UseShellExecute=false,CreateNoWindow=true,
                        RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                        StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
                    };
                    object gate=new object();var text=new StringBuilder();string terminal=null;bool quit=false,error=false;
                    Action stop=delegate {
                        lock(gate) {
                            if(quit)return;quit=true;
                            try {if(game!=null){process.StandardInput.WriteLine("quit");process.StandardInput.Flush();}else if(!process.HasExited)process.Kill();}
                            catch(InvalidOperationException){}catch(IOException){}
                        }
                    };
                    Action<string,bool> receive=delegate(string line,bool stderr) {
                        if(line==null)return;bool finish=false;
                        lock(gate) {
                            if(text.Length+line.Length<512*1024)text.AppendLine(line);
                            if(line.StartsWith("error:",StringComparison.OrdinalIgnoreCase)){error=true;finish=game!=null;}
                            if(game!=null&&!stderr) {
                                var done=ShaderProtocol.Terminal(line,game);
                                if(done!=null){terminal=done;finish=true;}
                                if(line=="queue not running"&&terminal==null){error=true;finish=true;}
                            }
                        }
                        try {log(line);}catch(InvalidOperationException){}
                        if(finish)stop();
                    };
                    process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){receive(e.Data,false);};
                    process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){receive(e.Data,true);};
                    process.Start();
                    try {job.Attach(process);process.PriorityClass=ProcessPriorityClass.Idle;}
                    catch {try {if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}throw;}
                    process.BeginOutputReadLine();process.BeginErrorReadLine();
                    using(ct.Register(stop)) {
                        if(game!=null) {
                            lock(gate) {if(!quit){process.StandardInput.WriteLine("add "+ShaderProtocol.GameId(game));process.StandardInput.WriteLine("start");process.StandardInput.WriteLine("list");process.StandardInput.Flush();}}
                        } else process.StandardInput.Close();
                        while(!process.WaitForExit(250)){} // wait for the owner, not a child's inherited output pipe
                        job.Dispose(); // remaining children must not hold stdout open after the CLI exited
                        process.WaitForExit(); // drain redirected streams before judging the result
                    }
                    ct.ThrowIfCancellationRequested();
                    if(process.ExitCode!=0 || error || game!=null&&terminal!="Done")throw new IOException("Движок не подтвердил завершение. Подробности — в журнале вкладки. Код: "+process.ExitCode);
                    return text.ToString();
                }
            });
        }
    }
}
