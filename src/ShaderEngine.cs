using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OpenAntiLag {
    public sealed class ShaderGame {
        public string Id, Name, Status, Reason;
        public bool CanCompile {get{return Status=="Ready" || Status=="Stale" || Status=="Warmed";}}
        public string StatusText {get {switch(Status) {case "Ready":return "Готова к подготовке";case "Stale":return "Кэш устарел";case "Warmed":return "Шейдеры подготовлены";case "NeedsRecording":return "Нужна запись игры";case "NeedsOfflineSession":return "Нужен офлайн-рекордер";default:return "Не поддерживается";}}}
        public string ReasonText {get {
            if(Status=="NeedsRecording"||Status=="NeedsOfflineSession")return "Нужен рекордер; в Open AntiLag он отключён.";
            if((Reason??"").Contains("no raw DXBC/DXIL shaders"))return "Движок пока не умеет читать шейдеры этой игры.";
            if((Reason??"").Contains("which its anti-cheat blocks"))return "Подготовка недоступна: нужный рекордер блокируется античитом.";
            if((Reason??"").Contains("not tested on this engine version"))return "Эта версия игрового движка ещё не проверена SCSKiller.";
            if((Reason??"").Contains("no recording needed"))return "Можно подготовить без запуска игры.";
            if((Reason??"").Contains("compiles every DirectX 11 shader"))return "Подготовка шейдеров DirectX 11 для NVIDIA.";
            return Reason;
        }}
    }
    public static class ShaderProtocol {
        static readonly Regex Row=new Regex(@"^(Unsupported|NeedsRecording|Ready|Warmed|Stale|NeedsOfflineSession)\s+(\S+)\s+(.+?)  - (.*)$");
        public static string GameId(string id) {
            if(id==null || !Regex.IsMatch(id,@"^[A-Za-z0-9][A-Za-z0-9:._!%+\-]{0,239}$"))throw new InvalidDataException("Некорректный идентификатор игры.");
            return id;
        }
        public static List<ShaderGame> Games(string output) {
            var result=new List<ShaderGame>();var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var line in (output??"").Split('\n')) {
                var m=Row.Match(line.TrimEnd('\r'));if(!m.Success)continue;
                var id=GameId(m.Groups[2].Value);
                if(!ids.Add(id))throw new InvalidDataException("Движок вернул повторяющуюся игру.");
                result.Add(new ShaderGame {Id=id,Status=m.Groups[1].Value,Name=m.Groups[3].Value.Trim(),Reason=m.Groups[4].Value.Trim()});
            }
            return result;
        }
        public static string Terminal(string line,string id) {
            var m=Regex.Match(line??"",@"^\d{2}:\d{2}:\d{2}\s+"+Regex.Escape(GameId(id))+@"\s+(Done|Failed|Stopped)(?:\s|$)");
            return m.Success?m.Groups[1].Value:null;
        }
    }
    // SCSKiller runs as an unmodified, separately downloaded program; no upstream code is linked.
    public sealed class ShaderEngine {
        public const string Version="1.2.5";
        public const string Source="https://github.com/BlueHeisenberg/SCSKiller";
        public const string DownloadUrl=Source+"/releases/download/v1.2.5/SCSKiller-1.2.5-Portable.zip";
        public const string ArchiveHash="a039c2fa82d36e78c82f5eaac436d46459c791bd1ff47e27be95037db2e4897c";
        const long MaxDownload=160L*1024*1024;
        public readonly string Root;
        public ShaderEngine(string root=null) {Root=Path.GetFullPath(root??Path.Combine(Program.DataDirectory,"shader-engine",Version));}
        public string Cli {get{return Path.Combine(Root,"current","cli","scskiller.exe");}}
        public bool Installed {get{return File.Exists(Cli)&&File.Exists(Path.Combine(Root,"module.zip"))&&File.Exists(Path.Combine(Root,".portable"))&&File.Exists(Path.Combine(Root,"data","migrated"));}}
        public static void CheckPlatform() {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT || Environment.OSVersion.Version.Major<10 || Environment.OSVersion.Version.Build<19041 || !Environment.Is64BitProcess)
                throw new NotSupportedException("Для подготовки шейдеров нужны Windows 10 2004 или новее / Windows 11 x64 и совместимая NVIDIA или AMD.");
            if(MachineWorker.IsAdmin)throw new InvalidOperationException("Подготовка шейдеров запускается без прав администратора. Перезапустите Open AntiLag обычным способом.");
        }
        static bool Trusted(Uri uri) {return uri.Scheme=="https"&&uri.IsDefaultPort&&String.IsNullOrEmpty(uri.UserInfo)&&(uri.Host=="github.com"||uri.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase));}
        FileStream OperationLock() {
            Directory.CreateDirectory(Path.GetDirectoryName(Root));
            try {return new FileStream(Root+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException error){throw new IOException("Модуль шейдеров уже занят другим процессом Open AntiLag.",error);}
        }
        public async Task Install(Action<string> log,CancellationToken ct) {
            using(var operation=OperationLock()) {
            CheckPlatform();if(Installed){log("Модуль уже установлен.");return;}
            string parent=Path.GetDirectoryName(Root);Directory.CreateDirectory(parent);
            string archive=Path.Combine(parent,"download-"+Guid.NewGuid().ToString("N")+".zip");
            try {
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                using(var handler=new HttpClientHandler {AllowAutoRedirect=false})
                using(var client=new HttpClient(handler) {Timeout=TimeSpan.FromMinutes(15)}) {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenAntiLag/0.6.2");var uri=new Uri(DownloadUrl);bool downloaded=false;
                    for(int redirect=0;redirect<6;redirect++) {
                        if(!Trusted(uri))throw new InvalidDataException("Недопустимый адрес модуля.");
                        using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct)) {
                            int status=(int)response.StatusCode;
                            if(status>=300&&status<400) {if(response.Headers.Location==null)throw new IOException("Пустое перенаправление.");uri=new Uri(uri,response.Headers.Location);continue;}
                            response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>MaxDownload)throw new IOException("Модуль слишком большой.");
                            using(var source=await response.Content.ReadAsStreamAsync())
                            using(var file=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                                byte[] buffer=new byte[65536];long total=0,last=0;int n;
                                while((n=await source.ReadAsync(buffer,0,buffer.Length,ct))>0) {
                                    total+=n;if(total>MaxDownload)throw new IOException("Превышен размер модуля.");await file.WriteAsync(buffer,0,n,ct);
                                    if(total-last>=4*1024*1024){last=total;log("Загружено "+(total/1024/1024)+" МБ из примерно 120 МБ…");}
                                }
                                file.Flush(true);
                            }
                            downloaded=true;break;
                        }
                    }
                    if(!downloaded)throw new IOException("Слишком много перенаправлений.");
                }
                log("Проверка SHA-256 и распаковка модуля…");
                await Task.Run(()=>InstallArchive(archive,ct),ct);
                log("SCSKiller "+Version+" готов. Рекордер и автозапуск отключены.");
            } finally {if(File.Exists(archive))File.Delete(archive);}
            }
        }
        public void InstallArchive(string archive,CancellationToken ct) {
            ct.ThrowIfCancellationRequested();Updates.Verify(archive,ArchiveHash);
            if(Directory.Exists(Root))throw new IOException("Папка модуля уже существует. Исходные данные сохранены; повторная установка поверх них запрещена.");
            string stage=Root+".partial-"+Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            try {
                Extract(archive,stage,ct);
                if(!File.Exists(Path.Combine(stage,"current","cli","scskiller.exe")) || !File.Exists(Path.Combine(stage,"current","native","scskiller_warm.exe")) || !File.Exists(Path.Combine(stage,"current","LICENSE")))
                    throw new InvalidDataException("Неожиданная структура официального модуля.");
                // Never import an existing SCSKiller profile: its recorder can be enabled by default.
                Directory.CreateDirectory(Path.Combine(stage,"data"));
                File.WriteAllText(Path.Combine(stage,".portable"),"");
                File.WriteAllText(Path.Combine(stage,"data","settings.json"),SafeSettings());
                File.WriteAllText(Path.Combine(stage,"data","migrated"),"OpenAntiLag isolated shader data");
                File.Copy(archive,Path.Combine(stage,"module.zip"));ct.ThrowIfCancellationRequested();
                Directory.Move(stage,Root);
            } finally {if(Directory.Exists(stage))Directory.Delete(stage,true);}
        }
        public static string SafeSettings() {
            return new JavaScriptSerializer().Serialize(new {
                Threads=Math.Max(1,Math.Min(4,Environment.ProcessorCount/2)),Priority="Idle",OnDriverUpdate="Off",BackgroundThreads=2,PauseWhileGaming=true,
                UseCommunityDb=false,MaximumPlans=false,StartWithWindows=false,MaxCompileMemoryGB=2,WelcomeSeen=true,ShareRecordings=false,
                SharePromptDismissed=true,RecordAllGames=false,RecordingLimitMB=128,NotifyNewShaders=false,ActiveCheck=false,
                InstallUpdatesAutomatically=false,ScanAtStart=true,CloseQuits=true,RecordingLimitChosen=true,TrayNoticeShown=true
            });
        }
        public static string EntryPath(string root,string name) {
            string relative=name.Replace('/',Path.DirectorySeparatorChar);
            if(String.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":")||relative.Split('\\','/').Any(x=>x==".."||x=="."||x.EndsWith(" ")||x.EndsWith(".")))throw new InvalidDataException("Недопустимый путь в архиве.");
            string path=Path.GetFullPath(Path.Combine(root,relative));
            if(!path.StartsWith(Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Выход за папку модуля.");
            return path;
        }
        public static void Extract(string archive,string root,CancellationToken ct) {
            using(var zip=ZipFile.OpenRead(archive)) {
                if(zip.Entries.Count>12000)throw new InvalidDataException("Слишком много файлов в модуле.");
                long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries) {
                    ct.ThrowIfCancellationRequested();string path=EntryPath(root,entry.FullName);
                    if(!names.Add(path))throw new InvalidDataException("Повторяющийся путь в архиве.");
                    if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Ссылки в архиве запрещены.");
                    total+=entry.Length;if(entry.Length>512L*1024*1024 || total>2L*1024*1024*1024)throw new InvalidDataException("Слишком большой распакованный модуль.");
                    if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(path);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var source=entry.Open())using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        byte[] buffer=new byte[65536];int n;long written=0;
                        while((n=source.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();written+=n;if(written>entry.Length)throw new InvalidDataException("Неверный размер файла.");target.Write(buffer,0,n);}
                        if(written!=entry.Length)throw new InvalidDataException("Неполный файл модуля.");
                    }
                }
            }
        }
        public void Verify(CancellationToken ct) {
            if(!Installed)throw new IOException("Сначала загрузите модуль SCSKiller.");
            RejectLinks(Root,ct);
            if(File.ReadAllText(Path.Combine(Root,"data","migrated"))!="OpenAntiLag isolated shader data")throw new IOException("Модуль использует чужую папку данных. Работа остановлена.");
            string archive=Path.Combine(Root,"module.zip");Updates.Verify(archive,ArchiveHash);
            using(var zip=ZipFile.OpenRead(archive)) foreach(var entry in zip.Entries.Where(e=>e.FullName.StartsWith("current/cli/",StringComparison.Ordinal)||e.FullName.StartsWith("current/native/",StringComparison.Ordinal))) {
                if(entry.FullName.EndsWith("/"))continue;ct.ThrowIfCancellationRequested();string target=EntryPath(Root,entry.FullName);
                if(!File.Exists(target)||new FileInfo(target).Length!=entry.Length)throw new IOException("Повреждён файл модуля: "+entry.FullName);
                using(var source=entry.Open())using(var hash=SHA256.Create()) {
                    string expected=BitConverter.ToString(hash.ComputeHash(source)).Replace("-","").ToLowerInvariant();Updates.Verify(target,expected);
                }
            }
            ValidateSettings(Path.Combine(Root,"data","settings.json"));
            string games=Path.Combine(Root,"data","games");
            if(Directory.Exists(games))foreach(string dir in Directory.GetDirectories(games)) {
                ct.ThrowIfCancellationRequested();string state=Path.Combine(dir,"state.json");if(!File.Exists(state))continue;
                var record=ReadJson(state);
                foreach(string key in new[]{"RecorderExe","RecorderMoveFrom","RecorderMoveTo","RecorderChained","OfflineSession"})if(record.ContainsKey(key)&&record[key]!=null)throw new IOException("В данных модуля обнаружен рекордер. Используйте отдельный SCSKiller для его восстановления.");
                if(record.ContainsKey("Recorder")&&record["Recorder"]!=null&&Convert.ToString(record["Recorder"])!="Off"&&Convert.ToString(record["Recorder"])!="Default")throw new IOException("Рекордер не поддерживается этим модулем.");
                if(record.ContainsKey("RecorderFiles")&&record["RecorderFiles"] is Dictionary<string,object>&&((Dictionary<string,object>)record["RecorderFiles"]).Count>0)throw new IOException("В модуле есть данные установленного рекордера. Работа остановлена.");
                if(record.ContainsKey("OfflineRecord")&&Object.Equals(record["OfflineRecord"],true))throw new IOException("Офлайн-рекордер не поддерживается этим модулем.");
            }
        }
        static void RejectLinks(string root,CancellationToken ct) {
            var paths=new Stack<string>();paths.Push(root);
            while(paths.Count>0) {ct.ThrowIfCancellationRequested();string path=paths.Pop();var attr=File.GetAttributes(path);
                if((attr&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки внутри папки модуля не поддерживаются.");
                if((attr&FileAttributes.Directory)!=0)foreach(var child in Directory.GetFileSystemEntries(path))paths.Push(child);
            }
        }
        static Dictionary<string,object> ReadJson(string path) {
            if(!File.Exists(path)||new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Отсутствуют или повреждены настройки модуля.");
            var data=new JavaScriptSerializer {MaxJsonLength=4*1024*1024}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;
            if(data==null)throw new InvalidDataException("Некорректные настройки модуля.");return data;
        }
        public static void ValidateSettings(string path) {
            var settings=ReadJson(path);
            foreach(string key in new[]{"RecordAllGames","UseCommunityDb","ShareRecordings","ActiveCheck","StartWithWindows","InstallUpdatesAutomatically","MaximumPlans"})
                if(!settings.ContainsKey(key)||!Object.Equals(settings[key],false))throw new InvalidDataException("Небезопасная настройка модуля: "+key+". Работа остановлена.");
            if(!settings.ContainsKey("Priority")||Convert.ToString(settings["Priority"])!="Idle"||!settings.ContainsKey("Threads")||Convert.ToInt32(settings["Threads"])<1||Convert.ToInt32(settings["Threads"])>4)
                throw new InvalidDataException("Изменены ограничения нагрузки модуля.");
            if(!settings.ContainsKey("OnDriverUpdate")||Convert.ToString(settings["OnDriverUpdate"])!="Off"||!settings.ContainsKey("PauseWhileGaming")||!Object.Equals(settings["PauseWhileGaming"],true)||!settings.ContainsKey("MaxCompileMemoryGB")||Convert.ToInt32(settings["MaxCompileMemoryGB"])!=2)
                throw new InvalidDataException("Изменены ограничения фоновой работы модуля.");
        }
        public async Task<string> Run(string command,string game,Action<string> log,CancellationToken ct) {
            CheckPlatform();
            if(command!="scan"&&command!="status"&&command!="cache"&&command!="queue")throw new InvalidOperationException("Недопустимая операция шейдеров.");
            if(command=="queue"||command=="status")ShaderProtocol.GameId(game);
            using(var operation=OperationLock()) {
                log("Проверка модуля и настроек безопасности…");await Task.Run(()=>Verify(ct),ct);
                string args=command=="status"?"status "+game:command=="cache"?"cache get":command;
                log(command=="scan"?"Поиск и анализ игр. Первый запуск может занять несколько минут…":"Запуск движка SCSKiller…");
                return await ShaderProcess.Run(Cli,args,command=="queue"?game:null,log,ct);
            }
        }
    }
}
