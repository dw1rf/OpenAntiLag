using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace OpenAntiLag {
    public sealed class NvidiaOption {
        public uint Id, Value;
        public string Name, Display;
        public bool AllowAbsent;
        public NvidiaOption(uint id,uint value,string name,string display,bool absent=false) {Id=id;Value=value;Name=name;Display=display;AllowAbsent=absent;}
    }
    public static class NvidiaPreset {
        public static List<NvidiaOption> Build(bool smooth,int fps,bool maxPower,bool fastTextures) {
            if(fps<0||fps>1000||smooth&&fps<20)throw new ArgumentOutOfRangeException("fps","Для VRR задайте лимит 20–1000 FPS ниже герцовки монитора.");
            return new List<NvidiaOption> {
                new NvidiaOption(0x007BA09E,1,"Очередь кадров","1 (Low Latency On)"),
                new NvidiaOption(0x10835000,0,"Ultra Low Latency","Выкл. (используется On)",true),
                new NvidiaOption(0x0005F543,1,"Low Latency в панели","Вкл.",true),
                new NvidiaOption(0x1057EB71,maxPower?1U:5U,"Управление питанием",maxPower?"Максимальная производительность":"Нормальный / Optimal power"),
                new NvidiaOption(0x0064B541,1,"Частота обновления","Наивысшая доступная"),
                new NvidiaOption(0x10835002,smooth?(uint)fps:0,"Лимит FPS",smooth?fps.ToString():"Выкл."),
                new NvidiaOption(0x10835016,0,"Фоновый лимит FPS","Выкл."),
                new NvidiaOption(0x20C1221E,0,"Потоковая оптимизация","Авто"),
                new NvidiaOption(0x20FDD1F9,0,"Тройная буферизация OpenGL","Выкл."),
                new NvidiaOption(0x00664339,0,"Ambient Occlusion","Выкл."),
                new NvidiaOption(0x10D2BB16,0,"Анизотропная фильтрация","Управление от приложения"),
                new NvidiaOption(0x107EFC5B,0,"Сглаживание — режим","Управление от приложения"),
                new NvidiaOption(0x1074C972,0,"FXAA","Выкл."),
                new NvidiaOption(0x10D48A85,0,"Сглаживание — прозрачность","Выкл."),
                new NvidiaOption(0x10FC2D9C,0,"Transparency multisampling","Выкл."),
                new NvidiaOption(0x107D639D,2,"Гамма-коррекция сглаживания","Вкл."),
                new NvidiaOption(0x0098C1AC,0,"MFAA","Выкл."),
                new NvidiaOption(0x00CE2691,fastTextures?20U:0U,"Качество фильтрации",fastTextures?"Высокая производительность":"Качество"),
                new NvidiaOption(0x00E73211,0,"Анизотропная оптимизация","Выкл."),
                new NvidiaOption(0x002ECAF2,1,"Трилинейная оптимизация","Вкл."),
                new NvidiaOption(0x0019BB68,1,"Отрицательное отклонение УД","Привязка / Clamp"),
                new NvidiaOption(0x00A879CF,smooth?0x47814940U:0x08416747U,"Вертикальный синхроимпульс",smooth?"Вкл.":"Выкл."),
                new NvidiaOption(0x1194F158,smooth?1U:0U,"Глобальная политика G-SYNC",smooth?"Полноэкранный режим":"Выкл."),
                new NvidiaOption(0x10A879CF,smooth?0U:4U,"Технология монитора",smooth?"Разрешить G-SYNC":"Фиксированная частота"),
                new NvidiaOption(0x10A879AC,smooth?0U:4U,"Запрос технологии монитора",smooth?"Разрешить G-SYNC":"Фиксированная частота")
            };
        }
        public static string Description(bool smooth,int fps,bool power,bool quality) {
            var b=new StringBuilder("Будут применены глобальные параметры NVIDIA:\r\n\r\n");
            foreach(var s in Build(smooth,fps,power,quality))b.AppendLine(s.Name+" → "+s.Display);
            b.AppendLine("\r\nПерезапустите игры после применения. Программные профили могут переопределять глобальные значения.");
            b.AppendLine("Кэш шейдеров, DSR, Image Scaling, CUDA, PhysX, разрешение и герцовка здесь не меняются. Reflex включается в игре.");
            if(smooth)b.AppendLine("G-SYNC монитора должен быть настроен заранее. Укажите FPS ниже его герцовки; выключите другие лимитеры. Эта кнопка задаёт политику 3D, а не проверяет поддержку VRR дисплеем.");
            if(power)b.AppendLine("Максимальная производительность может повысить расход, нагрев и шум.");
            if(quality)b.AppendLine("Высокая производительность фильтрации может ухудшить текстуры.");
            return b.ToString();
        }
    }
    public sealed class NvidiaBackupEntry {
        public uint Id, Original, Applied;
        public bool Existed, UserOverride;
    }
    public sealed class NvidiaBackup {
        public int Schema=1;
        public string Phase="Pending",Driver,Machine=Environment.MachineName;
        public List<NvidiaBackupEntry> Entries=new List<NvidiaBackupEntry>();
    }
    public interface INvidiaStore {
        NvidiaBackup Load();
        void Save(NvidiaBackup state);
        void Delete();
    }
    public sealed class NvidiaStore : INvidiaStore {
        readonly string path;
        public NvidiaStore(string path) {this.path=path;}
        public NvidiaBackup Load() {
            if(!File.Exists(path))return null;
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Повреждена резервная копия NVIDIA.");
            using(var reader=XmlReader.Create(path,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null })) {
                var state=(NvidiaBackup)new XmlSerializer(typeof(NvidiaBackup)).Deserialize(reader);
                NvidiaController.Validate(state);return state;
            }
        }
        public void Save(NvidiaBackup state) {
            NvidiaController.Validate(state);Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    new XmlSerializer(typeof(NvidiaBackup)).Serialize(stream,state);stream.Flush(true);
                }
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            } finally {if(File.Exists(temp))File.Delete(temp);}
        }
        public void Delete() { File.Delete(path); }
    }
    public sealed class NvidiaController {
        readonly Func<INvidiaDriver> open;
        readonly INvidiaStore store;
        public NvidiaController(Func<INvidiaDriver> open,INvidiaStore store) {this.open=open;this.store=store;}
        public static void Validate(NvidiaBackup state) {
            var ids=new HashSet<uint>(NvidiaPreset.Build(false,0,false,false).Select(x=>x.Id));
            if(state==null||state.Schema!=1||state.Machine!=Environment.MachineName||
                (state.Phase!="Pending"&&state.Phase!="Applied")||state.Entries==null||state.Entries.Count>ids.Count||
                state.Entries.Any(x=>x==null||!ids.Contains(x.Id))||state.Entries.Select(x=>x.Id).Distinct().Count()!=state.Entries.Count)
                throw new InvalidDataException("Резервная копия NVIDIA повреждена или принадлежит другому ПК.");
        }
        public string Inspect(List<NvidiaOption> options) {
            var state=store.Load();var b=new StringBuilder();
            using(var d=open()) {
                b.AppendLine("Драйвер NVIDIA "+d.Version+" • глобальные параметры");
                b.AppendLine(state==null?"Резервной копии нет. Можно применить выбранный набор.":"Есть резервная копия ("+state.Phase+"). Для смены набора сначала восстановите исходные значения.");
                foreach(var s in options) {
                    var v=d.Read(s.Id);
                    b.AppendLine(s.Name+": "+(v==null?"не задано": "0x"+v.Value.ToString("X8")+(v.UserOverride?" (пользовательское)":" (драйвер)"))+" → "+s.Display);
                }
            }
            return b.ToString();
        }
        public string Apply(List<NvidiaOption> options) {
            if(store.Load()!=null)throw new InvalidOperationException("Сначала восстановите предыдущий набор NVIDIA. Исходная копия сохранена.");
            if(options==null||options.Count!=NvidiaPreset.Build(false,0,false,false).Count)throw new InvalidOperationException("Неполный набор NVIDIA.");
            using(var d=open()) {
                var backup=new NvidiaBackup {Driver=d.Version};
                foreach(var s in options) {
                    var v=d.Read(s.Id);
                    if(v==null&&!s.AllowAbsent)throw new NotSupportedException("Драйвер не предоставил параметр: "+s.Name+". Ничего не применено.");
                    if(v!=null&&v.Value==s.Value)continue;
                    backup.Entries.Add(new NvidiaBackupEntry { Id=s.Id,Existed=v!=null,Original=v==null?0:v.Value,UserOverride=v!=null&&v.UserOverride,Applied=s.Value });
                }
                if(backup.Entries.Count==0)return "Все выбранные значения уже установлены. Изменений нет.";
                // Durable write-ahead journal: never overwrite an earlier user's original values.
                store.Save(backup);
                try {
                    foreach(var e in backup.Entries)d.Write(e.Id,e.Applied);
                    d.Save();
                    using(var verify=open())foreach(var e in backup.Entries) {
                        var v=verify.Read(e.Id);if(v==null||v.Value!=e.Applied)throw new IOException("Драйвер не подтвердил сохранение 0x"+e.Id.ToString("X8"));
                    }
                    backup.Phase="Applied";store.Save(backup);
                    return "Применено и проверено: "+backup.Entries.Count+" параметров NVIDIA. Перезапустите игры. Исходные значения сохранены.";
                } catch(Exception error) {
                    // A failed save may have committed some settings. Recovery uses a new session.
                    throw new InvalidOperationException("Применение NVIDIA не завершено: "+error.Message+" Резервная копия сохранена. Нажмите «Восстановить NVIDIA».",error);
                }
            }
        }
        public string Restore() {
            var backup=store.Load();if(backup==null)return "Нет изменений NVIDIA для восстановления.";Validate(backup);
            int restored=0,external=0;
            var changed=new List<NvidiaBackupEntry>();
            using(var d=open()) {
                foreach(var e in backup.Entries) {
                    var v=d.Read(e.Id);
                    if(IsOriginal(e,v))continue;
                    if(v==null||v.Value!=e.Applied) {external++;continue;}
                    if(e.UserOverride)d.Write(e.Id,e.Original);else d.Reset(e.Id);
                    changed.Add(e);restored++;
                }
                if(changed.Count>0)d.Save();
            }
            using(var verify=open())foreach(var e in changed) {
                if(!IsOriginal(e,verify.Read(e.Id)))throw new IOException("Восстановление не подтверждено. Резервная копия сохранена; повторите попытку.");
            }
            store.Delete();
            return "Восстановлено: "+restored+". Внешних изменений сохранено: "+external+". Перезапустите игры.";
        }
        static bool IsOriginal(NvidiaBackupEntry e,NvidiaValue v) {
            if(!e.Existed)return v==null;
            // Preserve inheritance; the driver's default can change after a driver update.
            if(!e.UserOverride)return v!=null&&!v.UserOverride;
            return v!=null&&v.UserOverride&&v.Value==e.Original;
        }
    }
}
