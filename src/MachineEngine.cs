using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace OpenAntiLag {
    public class MachineEntry {
        public string Id;
        public int? Original;
        public bool Attempted;
    }
    public class MachineState {
        public int Version=1;
        public string Phase="Disabled";
        public string OwnerSid;
        public string BootEntry;
        public string ChangeBoot;
        public string Error;
        public List<MachineEntry> Entries=new List<MachineEntry>();
    }
    public interface IMachineStore { MachineState Load(); void Save(MachineState state); }
    public interface IMachineHost {
        string BootEntry();
        string BootSession();
        void Preflight(bool bcdChanges);
        int? Read(string id,string bootEntry);
        void Write(string id,string bootEntry,int? value);
    }
    public static class MachineSettings {
        public static readonly string[] Ids={"useplatformclock","useplatformtick","disabledynamictick","hags","priority"};
        public static int? Target(string id) {
            switch(id) {
                case "useplatformclock": return null;
                case "useplatformtick": case "disabledynamictick": return 1;
                case "hags": return 2;
                case "priority": return 42;
                default: throw new InvalidDataException("Неизвестная системная настройка.");
            }
        }
        public static string[] Selection(IEnumerable<string> values) {
            var ids=(values??new string[0]).ToArray();
            if(ids.Length>Ids.Length||ids.Distinct().Count()!=ids.Length||ids.Any(x=>!Ids.Contains(x)))throw new InvalidDataException("Некорректный выбор системных настроек.");
            return ids;
        }
        public static bool HasBcd(IEnumerable<string> ids) {return ids.Any(x=>x!="hags"&&x!="priority");}
        public static void Validate(MachineState state) {
            if(state==null || (state.Version!=1&&state.Version!=2) || state.Entries==null || !new [] {"Disabled","Applying","Enabled","Restoring"}.Contains(state.Phase)) throw new InvalidDataException("Повреждён системный журнал. Автоматические изменения заблокированы.");
            if(state.Phase=="Disabled") { if(state.Entries.Count!=0)throw new InvalidDataException("Неверный журнал отключённого профиля."); return; }
            Guid guid;
            if(String.IsNullOrEmpty(state.OwnerSid)||((state.Version==1||HasBcd(state.Entries.Select(x=>x.Id)))&&!Guid.TryParse(state.BootEntry,out guid))||state.Entries.Count<1||state.Entries.Count>Ids.Length||(state.Version==1&&state.Entries.Count!=Ids.Length)||state.Entries.Select(x=>x.Id).Distinct().Count()!=state.Entries.Count) throw new InvalidDataException("Неполная системная резервная копия.");
            foreach(var entry in state.Entries) { Target(entry.Id); if(entry.Id!="priority" && entry.Original.HasValue && (entry.Original<0 || entry.Original>(entry.Id=="hags"?2:1))) throw new InvalidDataException("Недопустимое сохранённое значение."); }
        }
    }
    public sealed class MachineEngine {
        readonly IMachineHost host;
        readonly IMachineStore store;
        readonly string sid;
        public MachineState State {get;private set;}
        public MachineEngine(IMachineHost host,IMachineStore store,string sid) {this.host=host;this.store=store;this.sid=sid;State=store.Load();MachineSettings.Validate(State);}
        void CheckOwner() { if(State.OwnerSid!=sid)throw new InvalidOperationException("Системный профиль принадлежит другому пользователю. Отключите его из той учётной записи."); if((State.Version==1||MachineSettings.HasBcd(State.Entries.Select(x=>x.Id)))&&host.BootEntry()!=State.BootEntry)throw new InvalidOperationException("Вы загружены из другой записи Windows. Автоматический откат заблокирован."); }
        public void Enable() {
            // Legacy blanket enable stays blocked; only an explicit validated subset is accepted.
            throw new NotSupportedException("Автоматическое включение всех системных параметров запрещено. Выберите нужные настройки отдельными галочками.");
        }
        public void EnableSelected(IEnumerable<string> selected) {
            var ids=MachineSettings.Selection(selected);
            if(ids.Length==0)return;
            if(State.Phase!="Disabled")throw new InvalidOperationException("Сначала восстановите существующий системный профиль.");
            bool bcd=MachineSettings.HasBcd(ids);host.Preflight(bcd);
            string boot=host.BootSession();
            if(State.ChangeBoot==boot)throw new InvalidOperationException("Перед новым применением перезагрузите Windows.");
            var pending=new MachineState {Version=2,Phase="Applying",OwnerSid=sid,BootEntry=bcd?host.BootEntry():null};
            foreach(string id in ids)pending.Entries.Add(new MachineEntry {Id=id,Original=host.Read(id,pending.BootEntry)});
            MachineSettings.Validate(pending);store.Save(pending);State=pending;
            try {
                foreach(var entry in State.Entries) {
                    if(entry.Original==MachineSettings.Target(entry.Id))continue;
                    entry.Attempted=true;State.ChangeBoot=boot;store.Save(State);
                    host.Write(entry.Id,State.BootEntry,MachineSettings.Target(entry.Id));
                    if(host.Read(entry.Id,State.BootEntry)!=MachineSettings.Target(entry.Id))throw new IOException("Windows не сохранила настройку: "+entry.Id);
                }
                State.Phase="Enabled";store.Save(State);
            } catch(Exception error) {
                try {Disable();}catch(Exception rollback){throw new IOException(error.Message+"\nВосстановление не завершено: "+rollback.Message,error);}
                throw;
            }
        }
        public void Disable() {
            if(State.Phase=="Disabled")return; CheckOwner(); State.Phase="Restoring";store.Save(State);
            var errors=new List<string>();
            foreach(var entry in State.Entries.AsEnumerable().Reverse()) {
                if(!entry.Attempted)continue;
                try {
                    int? actual=host.Read(entry.Id,State.BootEntry);
                    if(actual==MachineSettings.Target(entry.Id) && actual!=entry.Original) {
                        if(entry.Id!="hags" && entry.Id!="priority")host.Preflight(true);
                        State.ChangeBoot=host.BootSession();store.Save(State);
                        host.Write(entry.Id,State.BootEntry,entry.Original);
                        if(host.Read(entry.Id,State.BootEntry)!=entry.Original)throw new IOException("Не удалось восстановить значение.");
                    }
                    entry.Attempted=false;store.Save(State);
                } catch(Exception error) {entry.Attempted=true;errors.Add(entry.Id+": "+error.Message);}
            }
            if(errors.Count>0) {State.Error=String.Join("\n",errors);store.Save(State);throw new IOException(State.Error);}
            var done=new MachineState {ChangeBoot=State.ChangeBoot};store.Save(done);State=done;
        }
    }
}
