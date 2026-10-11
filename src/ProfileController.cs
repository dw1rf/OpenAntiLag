using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAntiLag {
    public interface ISystemClient {
        MachineState State {get;}
        bool RebootPending {get;}
        void Disable();
    }
    public sealed class SystemClient : ISystemClient {
        readonly MachineStore store=new MachineStore();
        string boot;
        public MachineState State {get{return store.Load();}}
        // Clean installations do not need WMI or the BitLocker provider.
        public bool RebootPending {get {var state=State;if(state.ChangeBoot==null)return false;if(boot==null)boot=new MachineHost().BootSession();return state.ChangeBoot==boot;}}
        public void Disable() { if(State.Phase!="Disabled")MachineWorker.Elevate(false); }
    }
    // Used by the preview/test harness only; it cannot touch Windows settings.
    public sealed class PreviewSystemClient : ISystemClient {
        MachineState state=new MachineState();
        public MachineState State {get{return state;}}
        public bool RebootPending {get{return state.ChangeBoot!=null;}}
        public void Disable() {state=new MachineState();}
    }
    public sealed class ProfileController {
        readonly Engine user;
        readonly ISystemClient system;
        public ProfileController(Engine user,ISystemClient system) {this.user=user;this.system=system;}
        public void Enable(ProfileOptions options) {
            if(user.State.Phase!="Disabled" || system.State.Phase!="Disabled")throw new InvalidOperationException("Сначала отключите или восстановите существующий профиль.");
            if(system.RebootPending)throw new InvalidOperationException("После восстановления старого системного профиля сначала перезагрузите Windows.");
            user.Enable(options);
        }
        public void Resume() {
            if(user.State.SchemaVersion>=3 && system.State.Phase=="Disabled" && !system.RebootPending)user.Resume();
        }
        public void Disable() {
            var errors=new List<string>();
            try {if(system.State.Phase!="Disabled")system.Disable();} catch(OperationCanceledException) {throw;} catch(Exception error) {errors.Add(error.Message);}
            try {user.Disable();} catch(Exception error) {errors.Add(error.Message);}
            if(errors.Count>0)throw new IOException(String.Join("\n",errors));
        }
    }
}
