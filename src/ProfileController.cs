using System;
using System.Collections.Generic;
using System.IO;

namespace OpenAntiLag {
    public interface ISystemClient {
        MachineState State {get;}
        bool RebootPending {get;}
        void Enable();
        void Disable();
    }
    public sealed class SystemClient : ISystemClient {
        readonly MachineStore store=new MachineStore();
        readonly string boot=new MachineHost().BootSession();
        public MachineState State {get{return store.Load();}}
        public bool RebootPending {get {var state=State;return state.ChangeBoot!=null&&state.ChangeBoot==boot;}}
        public void Enable() { MachineWorker.Elevate(true); if(State.Phase!="Enabled")throw new IOException("Системный профиль не завершён."); }
        public void Disable() { if(State.Phase!="Disabled")MachineWorker.Elevate(false); }
    }
    // Used by the preview/test harness only; it cannot touch Windows settings.
    public sealed class PreviewSystemClient : ISystemClient {
        MachineState state=new MachineState();
        public MachineState State {get{return state;}}
        public bool RebootPending {get{return state.ChangeBoot!=null;}}
        public void Enable() {state=new MachineState {Phase="Enabled",ChangeBoot="preview"};}
        public void Disable() {state=new MachineState {ChangeBoot="preview"};}
    }
    public sealed class ProfileController {
        readonly Engine user;
        readonly ISystemClient system;
        public ProfileController(Engine user,ISystemClient system) {this.user=user;this.system=system;}
        public void Enable(ProfileOptions options) {
            if(user.State.Phase!="Disabled" || system.State.Phase!="Disabled")throw new InvalidOperationException("Сначала отключите или восстановите существующий профиль.");
            system.Enable();
            try {user.Enable(options);}
            catch(Exception original) {
                try {system.Disable();}
                catch(Exception rollback) {throw new IOException("Профиль не включён. Нужен повторный откат системной части.\n"+original.Message+"\n"+rollback.Message,original);}
                throw;
            }
        }
        public void Disable() {
            var errors=new List<string>();
            try {system.Disable();} catch(OperationCanceledException) {throw;} catch(Exception error) {errors.Add(error.Message);}
            try {user.Disable();} catch(Exception error) {errors.Add(error.Message);}
            if(errors.Count>0)throw new IOException(String.Join("\n",errors));
        }
    }
}
