using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class ReleaseAsset { public string name; public string browser_download_url; public string digest; public long size; }
    public sealed class ReleaseInfo { public string tag_name; public bool draft; public bool prerelease; public ReleaseAsset[] assets; }
    public sealed class UpdatePackage { public Version Version; public string Directory; public string Hash; }
    public sealed class UpdateTicket {
        public int Parent; public long ParentStarted; public string Destination; public string OriginalHash; public string Hash; public string Version; public bool Hidden;
    }
    public static class Updates {
        public const string Repository="https://github.com/dw1rf/OpenAntiLag";
        public const string Endpoint="https://api.github.com/repos/dw1rf/OpenAntiLag/releases/latest";
        public static readonly Version Current=new Version(0,5,6,0);
        const long MaxAsset=32*1024*1024;
        static string Preference {get{return Path.Combine(Program.DataDirectory,"updates.disabled");}}
        public static bool Enabled {get{return !File.Exists(Preference);} set {Directory.CreateDirectory(Program.DataDirectory);if(value) {if(File.Exists(Preference))File.Delete(Preference);}else File.WriteAllText(Preference,"disabled");}}
        public static ReleaseAsset Select(string json,Version current,out Version version) {
            version=null;
            if(json==null||json.Length>1024*1024)throw new InvalidDataException("Некорректный ответ GitHub.");
            var release=new JavaScriptSerializer().Deserialize<ReleaseInfo>(json);
            if(release==null||release.draft||release.prerelease)return null;
            if(!Regex.IsMatch(release.tag_name??"",@"^v\d+\.\d+\.\d+$"))return null;
            version=new Version(release.tag_name.Substring(1)+".0");
            if(version<=current)return null;
            ReleaseAsset chosen=null;
            foreach(var asset in release.assets??new ReleaseAsset[0]) if(asset!=null&&asset.name=="OpenAntiLag.exe") {if(chosen!=null)throw new InvalidDataException("Повторяющийся файл релиза.");chosen=asset;}
            if(chosen==null)throw new InvalidDataException("В релизе нет OpenAntiLag.exe.");
            string expected=Repository+"/releases/download/"+release.tag_name+"/OpenAntiLag.exe";
            if(chosen.browser_download_url!=expected || chosen.size<=0 || chosen.size>MaxAsset || !Regex.IsMatch(chosen.digest??"",@"^sha256:[a-fA-F0-9]{64}$"))throw new InvalidDataException("Релиз не прошёл проверку адреса, размера или SHA-256.");
            return chosen;
        }
        public static string Hash(string file) {using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        public static void Verify(string file,string hash) {if(!Regex.IsMatch(hash??"",@"^[a-fA-F0-9]{64}$")||!String.Equals(Hash(file),hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма обновления не совпадает.");}
        static bool Trusted(Uri url) {return url.Scheme=="https"&&url.IsDefaultPort&&String.IsNullOrEmpty(url.UserInfo)&&(url.Host=="api.github.com"||url.Host=="github.com"||url.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase));}
        static async Task<byte[]> Get(string address,long limit) {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            using(var handler=new HttpClientHandler {AllowAutoRedirect=false})using(var client=new HttpClient(handler) {Timeout=TimeSpan.FromSeconds(45),MaxResponseContentBufferSize=limit}) {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenAntiLag/0.5.6");
                Uri url=new Uri(address);
                for(int n=0;n<6;n++) {
                    if(!Trusted(url))throw new InvalidDataException("Недопустимый адрес обновления.");
                    using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseContentRead)) {
                        int status=(int)response.StatusCode;
                        if(status>=300&&status<400) {if(response.Headers.Location==null)throw new IOException("Пустой адрес перенаправления.");url=new Uri(url,response.Headers.Location);continue;}
                        response.EnsureSuccessStatusCode();
                        if(response.Content.Headers.ContentLength>limit)throw new IOException("Файл обновления слишком большой.");
                        var bytes=await response.Content.ReadAsByteArrayAsync();
                        if(bytes.LongLength>limit)throw new IOException("Файл обновления слишком большой.");return bytes;
                    }
                }
            }
            throw new IOException("Слишком много перенаправлений GitHub.");
        }
        public static async Task<UpdatePackage> Download(Version installed=null) {
            string json=System.Text.Encoding.UTF8.GetString(await Get(Endpoint,1024*1024));
            Version version;var asset=Select(json,installed??Current,out version);if(asset==null)return null;
            var bytes=await Get(asset.browser_download_url,MaxAsset);
            if(bytes.LongLength!=asset.size)throw new IOException("Неполный файл обновления.");
            string dir=Path.Combine(Program.DataDirectory,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string payload=Path.Combine(dir,"payload.exe");
            try {
                File.WriteAllBytes(payload,bytes);string hash=asset.digest.Substring(7);Verify(payload,hash);
                var assembly=AssemblyName.GetAssemblyName(payload);
                if(assembly.Name!="OpenAntiLag"||assembly.Version!=version)throw new InvalidDataException("Версия файла не совпадает с релизом.");
                return new UpdatePackage {Version=version,Directory=dir,Hash=hash};
            } catch {File.Delete(payload);throw;}
        }
        public static void StartInstall(UpdatePackage package,bool hidden) {
            if(MachineWorker.IsAdmin)throw new IOException("Автообновление доступно при обычном запуске без прав администратора.");
            string target=Application.ExecutablePath;
            string probe=Path.Combine(Path.GetDirectoryName(target),".openantilag-"+Guid.NewGuid().ToString("N"));
            using(File.Create(probe)){} File.Delete(probe);
            Verify(Path.Combine(package.Directory,"payload.exe"),package.Hash);
            using(var current=Process.GetCurrentProcess()) {
                var ticket=new UpdateTicket {Parent=current.Id,ParentStarted=current.StartTime.ToUniversalTime().Ticks,Destination=target,OriginalHash=Hash(target),Hash=package.Hash,Version=package.Version.ToString(),Hidden=hidden};
                File.WriteAllText(Path.Combine(package.Directory,"ticket.json"),new JavaScriptSerializer().Serialize(ticket));
            }
            string helper=Path.Combine(package.Directory,"apply.exe");File.Copy(target,helper,true);
            Process.Start(new ProcessStartInfo(helper,"--apply-update") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=package.Directory});
        }
        public static void Replace(string payload,string destination,string expectedOld,string expectedNew,Action<string> launch) {
            Verify(payload,expectedNew);Verify(destination,expectedOld);
            string backup=destination+".previous";
            if(File.Exists(backup))File.Delete(backup);
            string incoming=destination+".incoming-"+Guid.NewGuid().ToString("N");
            try {File.Copy(payload,incoming);Verify(incoming,expectedNew);File.Replace(incoming,destination,backup);}
            finally {if(File.Exists(incoming))File.Delete(incoming);}
            try {Verify(destination,expectedNew);launch(destination);}
            catch {File.Replace(backup,destination,null);throw;}
        }
        public static int Apply() {
            try {
                if(MachineWorker.IsAdmin)throw new IOException("Обновление не запускается с повышенными правами.");
                string folder=Path.GetDirectoryName(Application.ExecutablePath);
                string ticketFile=Path.Combine(folder,"ticket.json");
                if(new FileInfo(ticketFile).Length>8192)throw new InvalidDataException("Неверный запрос обновления.");
                var ticket=new JavaScriptSerializer().Deserialize<UpdateTicket>(File.ReadAllText(ticketFile));
                string target=Path.GetFullPath(ticket.Destination),payload=Path.Combine(folder,"payload.exe");
                if(!Path.IsPathRooted(ticket.Destination)||!target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||String.Equals(Path.GetDirectoryName(target),folder,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Недопустимое расположение программы.");
                try {using(var parent=Process.GetProcessById(ticket.Parent)) {if(parent.StartTime.ToUniversalTime().Ticks!=ticket.ParentStarted)throw new IOException("Процесс приложения изменился.");if(!parent.WaitForExit(60000))throw new IOException("Приложение ещё работает. Обновление отложено.");}}catch(ArgumentException){}
                Verify(payload,ticket.Hash);
                var assembly=AssemblyName.GetAssemblyName(payload);
                if(assembly.Name!="OpenAntiLag"||assembly.Version!=new Version(ticket.Version)||assembly.Version<=Current)throw new InvalidDataException("Недопустимая версия обновления.");
                Replace(payload,target,ticket.OriginalHash,ticket.Hash,delegate(string path) {Process.Start(new ProcessStartInfo(path,ticket.Hidden?"--tray":"") {UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)});});
                File.Delete(ticketFile);return 0;
            }catch(Exception error) {Program.Log("Update failed: "+error);MessageBox.Show("Обновление не установлено. Предыдущая версия сохранена.\n"+error.Message,"Open AntiLag",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
        }
    }
}
