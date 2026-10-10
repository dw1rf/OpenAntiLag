using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace OpenAntiLag {
    public sealed class AmdEntry {public string Key;public int Original,Target;}
    public sealed class AmdBackup {public int Schema=1;public string Machine=Environment.MachineName,Gpu,Phase="Pending";public bool SkipGroup;public List<AmdEntry> Entries=new List<AmdEntry>();}
    public interface IAmdStore {AmdBackup Load();void Save(AmdBackup value);void Delete();}
    public sealed class AmdStore:IAmdStore {
        readonly string path;public AmdStore(string path){this.path=path;}
        public AmdBackup Load(){if(!File.Exists(path))return null;if(new FileInfo(path).Length>65536)throw new InvalidDataException("Повреждена копия AMD.");using(var r=XmlReader.Create(path,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})){var b=(AmdBackup)new XmlSerializer(typeof(AmdBackup)).Deserialize(r);AmdProfiles.Validate(b);return b;}}
        public void Save(AmdBackup value){AmdProfiles.Validate(value);Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){new XmlSerializer(typeof(AmdBackup)).Serialize(f,value);f.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        public void Delete(){File.Delete(path);}
    }
    public sealed class AmdProfiles {
        public static readonly string[] Keys={"chill","boost","antilag","enhanced","morphological","vsync"};
        public static string Name(string key){switch(key){case "chill":return "Radeon Chill";case "boost":return "Radeon Boost";case "antilag":return "Radeon Anti-Lag";case "enhanced":return "Enhanced Sync";case "morphological":return "Морфологическое сглаживание";default:return "Ожидание вертикального обновления";}}
        public static string Value(string key,int v){return key=="vsync"?new[]{"Всегда выкл.","Выкл., если не задано игрой","Вкл., если не задано игрой","Всегда вкл."}[v]:v==0?"Выкл.":"Вкл.";}
        public static void ValidateValue(string key,int value){if(!Keys.Contains(key)||value<0||value>(key=="vsync"?3:1))throw new InvalidDataException("Некорректный параметр AMD.");}
        public static void Validate(AmdBackup b){if(b==null||b.Schema!=1||b.Machine!=Environment.MachineName||String.IsNullOrWhiteSpace(b.Gpu)||!(new[]{"Pending","Applied","Restoring"}.Contains(b.Phase))||b.Entries==null||b.Entries.Count>Keys.Length||b.Entries.Select(x=>x.Key).Distinct().Count()!=b.Entries.Count)throw new InvalidDataException("Копия AMD повреждена или принадлежит другому ПК.");foreach(var e in b.Entries){ValidateValue(e.Key,e.Original);ValidateValue(e.Key,e.Target);}}
        static bool Group(string k){return k=="antilag"||k=="boost"||k=="chill";}
        readonly Func<IAmdDriver> open;readonly IAmdStore store;
        public AmdProfiles(Func<IAmdDriver> open,IAmdStore store){this.open=open;this.store=store;}
        public List<AmdGpu> Devices(){using(var d=open())return d.Devices;}
        public string Inspect(string gpu){using(var d=open()){d.Select(gpu);var b=new StringBuilder();foreach(var k in Keys){var v=d.Read(k);if(v.HasValue)ValidateValue(k,v.Value);b.AppendLine(Name(k)+": "+(v.HasValue?Value(k,v.Value):"не поддерживается — будет пропущено"));}b.AppendLine(store.Load()==null?"Исходная копия ещё не создана.":"Есть копия AMD. Перед новым набором выполните восстановление.");return b.ToString();}}
        public string Apply(string gpu,bool antiLag,bool sync){if(store.Load()!=null)throw new InvalidOperationException("Сначала восстановите предыдущий набор AMD.");using(var d=open()){d.Select(gpu);var b=new AmdBackup {Gpu=gpu};foreach(var k in Keys){var v=d.Read(k);if(v.HasValue){ValidateValue(k,v.Value);b.Entries.Add(new AmdEntry {Key=k,Original=v.Value,Target=k=="antilag"&&antiLag?1:k=="vsync"?(sync?1:0):0});}}
            if(b.Entries.Count==0)throw new NotSupportedException("Ни один из параметров не поддерживается драйвером AMD.");
            if(b.Entries.All(x=>x.Original==x.Target))return "Все поддерживаемые значения уже установлены.";
            store.Save(b);
            try {foreach(var e in b.Entries.Where(x=>Group(x.Key)))if(d.Read(e.Key)!=0)d.Write(e.Key,0);foreach(var e in b.Entries)if(d.Read(e.Key)!=e.Target)d.Write(e.Key,e.Target);foreach(var e in b.Entries)if(d.Read(e.Key)!=e.Target)throw new IOException("AMD не подтвердила "+Name(e.Key));b.Phase="Applied";store.Save(b);return "AMD: значения применены и прочитаны обратно. Поддерживается "+b.Entries.Count+" из "+Keys.Length+". Перезапустите игры.";}catch(Exception ex){throw new IOException("Применение AMD прервано: "+ex.Message+" Исходная копия сохранена. Нажмите «Восстановить AMD».",ex);}
        }}
        public string Restore(){var b=store.Load();if(b==null)return "Нет изменений AMD для восстановления.";Validate(b);using(var d=open()){d.Select(b.Gpu);var current=new Dictionary<string,int>();foreach(var e in b.Entries){var v=d.Read(e.Key);if(!v.HasValue)throw new NotSupportedException("Восстановление недоступно для "+Name(e.Key)+". Копия сохранена.");current[e.Key]=v.Value;}
            var group=b.Entries.Where(x=>Group(x.Key)).ToList();
            b.SkipGroup=b.SkipGroup||group.Any(e=>current[e.Key]!=e.Original&&current[e.Key]!=e.Target&&!(b.Phase!="Applied"&&current[e.Key]==0));
            b.Phase="Restoring";store.Save(b);int external=b.SkipGroup?group.Count:0;
            if(!b.SkipGroup&&!group.All(e=>current[e.Key]==e.Original)){foreach(var e in group)if(d.Read(e.Key)!=0)d.Write(e.Key,0);foreach(var e in group)if(e.Original!=0)d.Write(e.Key,e.Original);foreach(var e in group)if(d.Read(e.Key)!=e.Original)throw new IOException("AMD не подтвердила восстановление Anti-Lag/Chill/Boost. Копия сохранена.");}
            foreach(var e in b.Entries.Where(x=>!Group(x.Key))){int v=current[e.Key];if(v==e.Original)continue;if(v!=e.Target){external++;continue;}d.Write(e.Key,e.Original);if(d.Read(e.Key)!=e.Original)throw new IOException("Восстановление не подтверждено. Копия сохранена.");}
            store.Delete();return "Исходные значения AMD восстановлены. Сохранено внешних изменений/связанных параметров: "+external+". Перезапустите игры.";
        }}
        public static string Guide(bool sync,bool antiLag){return "AMD • Глобальные параметры выбранной Radeon\r\n\r\n"+(sync?"РЕЖИМ: БЕЗ РАЗРЫВОВ":"РЕЖИМ: МИНИМАЛЬНАЯ ЗАДЕРЖКА")+"\r\n\r\nRadeon Anti-Lag: "+(antiLag?"Вкл.":"Выкл.")+"\r\nRadeon Chill: Выкл.\r\nRadeon Boost: Выкл.\r\nEnhanced Sync: Выкл.\r\nМорфологическое сглаживание: Выкл.\r\nОжидание вертикального обновления: "+(sync?"Выкл., если не задано игрой":"Всегда выкл.")+"\r\n\r\nПрименяются только поддерживаемые параметры через ADLX. Встроенная и дискретная Radeon выбираются отдельно; профили отдельных игр могут переопределять глобальные настройки.\r\n\r\nРежим V-Sync драйвера не заменяет настройки DirectX/Vulkan-игры. Для игры без разрывов включите FreeSync монитора и V-Sync в игре, задайте один лимит FPS ниже герцовки. FreeSync и лимит FPS эта кнопка не меняет.\r\n\r\nAnti-Lag 2 включается в поддерживаемой игре. Если используете его, снимите флажок обычного Anti-Lag. AFMF, RSR, HYPR-RX, фильтрация текстур, разгон, разрешение и частота дисплея здесь не меняются. Для киберспортивного исходного сравнения генерацию кадров отключите отдельно.\r\n\r\nПеред записью сохраняется копия. Восстановление связано с выбранной GPU. Anti-Lag/Chill/Boost восстанавливаются группой из-за взаимного отключения функций. При внешнем изменении группы она сохраняется целиком.\r\n\r\nПоддержка AMD экспериментальная: на реальной Radeon не проверена. Закройте игры и Adrenalin перед применением; не меняйте настройки другой утилитой во время операции.\r\n\r\nИсточники: https://gpuopen.com/adlx/\r\nhttps://www.amd.com/en/resources/support-articles/faqs/DH3-012.html\r\n";}
    }
}
