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
        public static void Validate(MachineState state) {
            if(state==null || state.Version!=1 || state.Entries==null || !new [] {"Disabled","Applying","Enabled","Restoring"}.Contains(state.Phase)) throw new InvalidDataException("Повреждён системный журнал. Автоматические изменения заблокированы.");
            if(state.Phase=="Disabled") { if(state.Entries.Count!=0)throw new InvalidDataException("Неверный журнал отключённого профиля."); return; }
            Guid guid;
            if(String.IsNullOrEmpty(state.OwnerSid)||!Guid.TryParse(state.BootEntry,out guid)||state.Entries.Count!=Ids.Length || state.Entries.Select(x=>x.Id).Distinct().Count()!=Ids.Length) throw new InvalidDataException("Неполная системная резервная копия.");
            foreach(var entry in state.Entries) { Target(entry.Id); if(entry.Id!="priority" && entry.Original.HasValue && (entry.Original<0 || entry.Original>(entry.Id=="hags"?2:1))) throw new InvalidDataException("Недопустимое сохранённое значение."); }
        }
    }
    public sealed class MachineEngine {
        readonly IMachineHost host;
        readonly IMachineStore store;
        readonly string sid;
        public MachineState State {get;private set;}
        public MachineEngine(IMachineHost host,IMachineStore store,string sid) {this.host=host;this.store=store;this.sid=sid;State=store.Load();MachineSettings.Validate(State);}
        void CheckOwner() { if(State.OwnerSid!=sid)throw new InvalidOperationException("Системный профиль принадлежит другому пользователю. Отключите его из той учётной записи."); if(host.BootEntry()!=State.BootEntry)throw new InvalidOperationException("Вы загружены из другой записи Windows. Автоматический откат заблокирован."); }
        public void Enable() {
            // Retained only to reject old callers; system settings are restore-only since 0.5.7.
            throw new NotSupportedException("Новые изменения BCD, HAGS и системных приоритетов отключены. Доступно только восстановление старого профиля.");
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
