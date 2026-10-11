using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OpenAntiLag {
    public static class ShaderTests {
        public const string Listing="GPU: Test GPU, driver 1.0\nReady               steam:42                 Тестовая игра ™  - shaders available\nNeedsRecording      epic:abc-def             Other game  - recording required\nStale               steam:43                 Старый кэш  - driver changed\nWarmed              steam:44                 Warm game  - up to date\n";
        static int count;
        static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        static void Throws(Action action){bool threw=false;try{action();}catch{threw=true;}Assert(threw,"Expected rejection");}
        static void Test(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        static void Temp(Action<string> action){string root=Path.Combine(Path.GetTempPath(),"OpenAntiLag-shader-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{action(root);}finally{Directory.Delete(root,true);}}
        static string Executable {get{return System.Windows.Forms.Application.ExecutablePath;}}
        static string RunFixture(string mode,string id=null,CancellationToken ct=default(CancellationToken),Action<string> log=null){return ShaderProcess.Run(Executable,"--shader-fixture "+mode,id,log??delegate{},ct).GetAwaiter().GetResult();}
        public static int Fixture(string mode) {
            Console.OutputEncoding=new UTF8Encoding(false);
            if(mode=="scan"){Console.Write(Listing);return 0;}
            if(mode=="child"){Thread.Sleep(30000);return 0;}
            if(mode=="spawn") {using(var child=Process.Start(new ProcessStartInfo(Executable,"--shader-fixture child"){UseShellExecute=false,CreateNoWindow=true})){Console.WriteLine("CHILD "+child.Id);}return 0;}
            if(mode=="unexpected")return 0;
            string line;
            while((line=Console.ReadLine())!=null) {
                if(line=="quit"){Console.WriteLine("GRACEFUL STOP");return 0;}
                if(line=="start") {
                    if(mode=="error")Console.Error.WriteLine("error: simulated rejection");
                    else Console.WriteLine("12:34:56 steam:42 "+(mode=="success"?"Done":mode=="failed"?"Failed - rejected":"Warming 1/10"));
                }
                if(line=="list"&&mode=="empty")Console.WriteLine("queue not running");
            }
            return 0;
        }
        public static int Run(){
            Test("Shader list parses Unicode, status, reason and driver staleness",delegate {var games=ShaderProtocol.Games(Listing);Assert(games.Count==4&&games[0].Name=="Тестовая игра ™"&&games[0].Id=="steam:42"&&games[2].Status=="Stale"&&games[2].CanCompile&&!games[1].CanCompile,"Wrong game list");});
            Test("Shader list rejects duplicate game IDs",delegate {Throws(()=>ShaderProtocol.Games(Listing+Listing));});
            Test("Shader command rejects option and newline injection",delegate {foreach(var id in new[]{"--all-ready","steam:42\nquit","steam:42 start","\"steam:42\"","","../game"})Throws(()=>ShaderProtocol.GameId(id));Assert(ShaderProtocol.GameId("manual:abc-def")=="manual:abc-def","Valid ID rejected");});
            Test("Shader terminal status cannot match another game or reason",delegate {Assert(ShaderProtocol.Terminal("12:34:56 steam:420 Done","steam:42")==null&&ShaderProtocol.Terminal("note steam:42 Done","steam:42")==null&&ShaderProtocol.Terminal("12:34:56 steam:42 Done","steam:42")=="Done","False completion");});
            Test("Shader extraction blocks path traversal and NTFS alternate streams",delegate {Temp(root=>{foreach(var name in new[]{"../outside","a/../../outside","C:/outside","/outside","a:stream","a./file","a /file"})Throws(()=>ShaderEngine.EntryPath(root,name));Assert(ShaderEngine.EntryPath(root,"current/cli/tool.exe").StartsWith(root+Path.DirectorySeparatorChar),"Valid path rejected");});});
            Test("Shader extraction rejects duplicates before overwriting files",delegate {Temp(root=>{string file=Path.Combine(root,"module.zip"),target=Path.Combine(root,"out");Directory.CreateDirectory(target);using(var zip=ZipFile.Open(file,ZipArchiveMode.Create)){using(var w=new StreamWriter(zip.CreateEntry("tool.txt").Open()))w.Write("first");using(var w=new StreamWriter(zip.CreateEntry("TOOL.txt").Open()))w.Write("second");}Throws(()=>ShaderEngine.Extract(file,target,CancellationToken.None));Assert(File.ReadAllText(Path.Combine(target,"tool.txt"))=="first","Duplicate overwrote target");});});
            Test("Shader extraction copies valid files and honors cancellation",delegate {Temp(root=>{string file=Path.Combine(root,"module.zip"),target=Path.Combine(root,"out");Directory.CreateDirectory(target);using(var zip=ZipFile.Open(file,ZipArchiveMode.Create))using(var w=new StreamWriter(zip.CreateEntry("current/cli/a.txt").Open()))w.Write("hello");ShaderEngine.Extract(file,target,CancellationToken.None);Assert(File.ReadAllText(Path.Combine(target,"current","cli","a.txt"))=="hello","Bad extraction");var ct=new CancellationToken(true);Throws(()=>ShaderEngine.Extract(file,target,ct));});});
            Test("Shader archive hash is checked before installation",delegate {Temp(root=>{var file=Path.Combine(root,"bad.zip");File.WriteAllText(file,"untrusted");var engine=new ShaderEngine(Path.Combine(root,"engine"));Throws(()=>engine.InstallArchive(file,CancellationToken.None));Assert(!Directory.Exists(engine.Root),"Unverified module installed");});});
            Test("Shader settings disable recorder, sharing, autostart and background checks",delegate {Temp(root=>{string file=Path.Combine(root,"settings.json");File.WriteAllText(file,ShaderEngine.SafeSettings());ShaderEngine.ValidateSettings(file);var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));Assert((int)data["Threads"]<=4&&(int)data["MaxCompileMemoryGB"]==2,"Unbounded resource defaults");});});
            Test("Shader settings refuse re-enabled recorder and missing safety flags",delegate {Temp(root=>{string file=Path.Combine(root,"settings.json");foreach(var flag in new[]{"RecordAllGames","ShareRecordings","ActiveCheck","StartWithWindows","UseCommunityDb","MaximumPlans","InstallUpdatesAutomatically"}) {var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(ShaderEngine.SafeSettings());data[flag]=true;File.WriteAllText(file,new JavaScriptSerializer().Serialize(data));Throws(()=>ShaderEngine.ValidateSettings(file));data.Remove(flag);File.WriteAllText(file,new JavaScriptSerializer().Serialize(data));Throws(()=>ShaderEngine.ValidateSettings(file));}});});
            Test("Shader engine rejects arbitrary commands before starting a process",delegate {Temp(root=>{var engine=new ShaderEngine(root);Throws(()=>engine.Run("record",null,delegate{},CancellationToken.None).GetAwaiter().GetResult());Assert(!engine.Installed,"Unexpected engine");});});
            Test("Shader process reads UTF8 output without a console window",delegate {Assert(ShaderProtocol.Games(RunFixture("scan")).First().Name=="Тестовая игра ™","UTF8 lost");});
            Test("Shader queue confirms Done and exits gracefully",delegate {var text=RunFixture("success","steam:42");Assert(text.Contains("Done")&&text.Contains("GRACEFUL STOP"),"Queue did not stop gracefully");});
            Test("Shader queue rejects failed, empty and unconfirmed completion",delegate {foreach(var mode in new[]{"failed","error","empty","unexpected"})Throws(()=>RunFixture(mode,"steam:42"));});
            Test("Shader cancellation sends quit and waits for graceful exit",delegate {using(var ct=new CancellationTokenSource()){bool graceful=false;try{RunFixture("cancel","steam:42",ct.Token,line=>{if(line.Contains("Warming"))ct.Cancel();if(line=="GRACEFUL STOP")graceful=true;});throw new Exception("Cancellation not reported");}catch(OperationCanceledException){}Assert(graceful,"Child was killed instead of graceful cancellation");}});
            Test("Shader job cleans up only its own child processes",delegate {var text=RunFixture("spawn");int pid=Int32.Parse(text.Trim().Substring(6));try {using(var process=Process.GetProcessById(pid))Assert(process.WaitForExit(5000),"Orphan child remained");}catch(ArgumentException){}});
            return count;
        }
        public static int ModuleCheck(string archive,string root) {
            try {
                var engine=new ShaderEngine(root);if(!engine.Installed)engine.InstallArchive(archive,CancellationToken.None);
                engine.Verify(CancellationToken.None);
                string help=ShaderProcess.Run(engine.Cli,"--help",null,Console.WriteLine,CancellationToken.None).GetAwaiter().GetResult();
                Assert(help.Contains("queue")&&help.Contains("scan"),"Unexpected CLI protocol");
                Console.WriteLine("PASS official SCSKiller archive, dependency verification, isolated settings and native CLI startup; no shader compilation performed.");return 0;
            }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
