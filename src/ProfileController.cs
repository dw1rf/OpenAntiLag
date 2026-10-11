using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAntiLag {
    public interface ISystemClient {
        MachineState State {get;}
        bool RebootPending {get;}
        void Disable();
    }
    public interface IAdvancedSystemClient : ISystemClient {void EnableSelected(string[] ids);}
    public sealed class SystemClient : IAdvancedSystemClient {
        readonly MachineStore store=new MachineStore();
        string boot;
        public MachineState State {get{return store.Load();}}
        // Clean installations do not need WMI or the BitLocker provider.
        public bool RebootPending {get {var state=State;if(state.ChangeBoot==null)return false;if(boot==null)boot=new MachineHost().BootSession();return state.ChangeBoot==boot;}}
        public void EnableSelected(string[] ids) {MachineWorker.ElevateSelected(ids);}
        public void Disable() { if(State.Phase!="Disabled")MachineWorker.Elevate(false); }
    }
    // Used by the preview/test harness only; it cannot touch Windows settings.
    public sealed class PreviewSystemClient : IAdvancedSystemClient {
        MachineState state=new MachineState();
        public MachineState State {get{return state;}}
        public bool RebootPending {get{return state.ChangeBoot!=null;}}
        public void EnableSelected(string[] ids) {state=new MachineState {Version=2,Phase="Enabled",Entries=new List<MachineEntry>()};foreach(string id in ids)state.Entries.Add(new MachineEntry {Id=id});}
        public void Disable() {state=new MachineState();}
    }
    public sealed class ProfileController {
        readonly Engine user;
        readonly ISystemClient system;
        public ProfileController(Engine user,ISystemClient system) {this.user=user;this.system=system;}
        public void Enable(ProfileOptions options) {Enable(options,new string[0]);}
        public void Enable(ProfileOptions options,string[] selected) {
            var ids=MachineSettings.Selection(selected);
            if(user.State.Phase!="Disabled" || system.State.Phase!="Disabled")throw new InvalidOperationException("Сначала отключите или восстановите существующий профиль.");
            if(system.RebootPending)throw new InvalidOperationException("После изменения системных параметров сначала перезагрузите Windows.");
            if(ids.Length==0){user.Enable(options);return;}
            var advanced=system as IAdvancedSystemClient;if(advanced==null)throw new NotSupportedException("Системный клиент не поддерживает выбранные настройки.");
            advanced.EnableSelected(ids);
            try {user.Enable(options);}catch(Exception error) {try{system.Disable();}catch(Exception rollback){throw new IOException(error.Message+"\nСистемный откат не завершён: "+rollback.Message,error);}throw;}

        }
        public void Resume() {
            if(user.State.SchemaVersion>=3 && (system.State.Phase=="Disabled" || system.State.Version==2&&system.State.Phase=="Enabled") && !system.RebootPending)user.Resume();
        }
        public void Disable() {
            var errors=new List<string>();
            try {if(system.State.Phase!="Disabled")system.Disable();} catch(OperationCanceledException) {throw;} catch(Exception error) {errors.Add(error.Message);}
            try {user.Disable();} catch(Exception error) {errors.Add(error.Message);}
            if(errors.Count>0)throw new IOException(String.Join("\n",errors));
        }
    }
}
