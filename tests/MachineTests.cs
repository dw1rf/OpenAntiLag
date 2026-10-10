using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace OpenAntiLag {
    public sealed class FakeMachineHost : IMachineHost {
        public string Entry="{22222222-2222-2222-2222-222222222222}";
        public string Boot="boot-1";
        public readonly Dictionary<string,int?> Values=new Dictionary<string,int?> { {"useplatformclock",1},{"useplatformtick",null},{"disabledynamictick",0},{"hags",1},{"priority",38} };
        public string Fail;
        public bool Block;
        public bool Ignore;
        public int Writes;
        public string BootEntry(){return Entry;}
        public string BootSession(){return Boot;}
        public void Preflight(bool changes){if(Block&&changes)throw new InvalidOperationException("BitLocker blocked");}
        public int? Read(string id,string entry){return Values[id];}
        public void Write(string id,string entry,int? value){Writes++;if(!Ignore)Values[id]=value;if(Fail==id){Fail=null;throw new IOException("Injected failure "+id);}}
    }
    public sealed class MemoryMachineStore : IMachineStore {
        MachineState state=new MachineState();
        public bool Fail;
        static MachineState Copy(MachineState value){var serializer=new XmlSerializer(typeof(MachineState));using(var stream=new MemoryStream()){serializer.Serialize(stream,value);stream.Position=0;return (MachineState)serializer.Deserialize(stream);}}
        public MachineState Load(){return Copy(state);}
        public void Save(MachineState value){if(Fail)throw new IOException("Disk full");state=Copy(value);}
    }
    public sealed class TestSystemClient : ISystemClient {
        public MachineState Value=new MachineState();
        public bool CancelEnable,CancelDisable,FailDisable;
        public MachineState State {get{return Value;}}
        public bool RebootPending {get{return false;}}
        public void Enable(){if(CancelEnable)throw new OperationCanceledException();Value.Phase="Enabled";}
        public void Disable(){if(CancelDisable)throw new OperationCanceledException();if(FailDisable)throw new IOException("System restore failed");Value.Phase="Disabled";}
    }
    public static class MachineTests {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Throws(Action action){bool failed=false;try{action();}catch{failed=true;}Check(failed,"Expected failure");}
        static void Test(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        static void Originals(FakeMachineHost h){Check(h.Values["useplatformclock"]==1&&h.Values["useplatformtick"]==null&&h.Values["disabledynamictick"]==0&&h.Values["hags"]==1&&h.Values["priority"]==38,"System originals not restored");}
        public static int Run(){
            Test("System profile exact BCD/HAGS/priority restoration",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();foreach(string id in MachineSettings.Ids)Check(h.Values[id]==MachineSettings.Target(id),"Target mismatch");Check(s.Load().ChangeBoot=="boot-1","Missing restart state");e.Disable();Originals(h);Check(s.Load().Phase=="Disabled","Not disabled");});
            foreach(string id in MachineSettings.Ids){string point=id;Test("System partial failure: "+point,delegate {var h=new FakeMachineHost {Fail=point};var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");Throws(e.Enable);Originals(h);Check(s.Load().Phase=="Disabled","Rollback not completed");});}
            Test("System preflight refusal has no writes",delegate {var h=new FakeMachineHost {Block=true};var s=new MemoryMachineStore();Throws(new MachineEngine(h,s,"owner").Enable);Check(h.Writes==0&&s.Load().Phase=="Disabled","Changed system despite protection");});
            Test("System journal write failure blocks all changes",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore {Fail=true};Throws(new MachineEngine(h,s,"owner").Enable);Check(h.Writes==0,"Write before durable snapshot");});
            Test("BitLocker enabled later blocks BCD restore but restores other values",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();h.Block=true;Throws(e.Disable);Check(h.Values["hags"]==1&&h.Values["priority"]==38&&h.Values["useplatformtick"]==1&&s.Load().Phase=="Restoring","Unsafe BCD restore");h.Block=false;e.Disable();Originals(h);});
            Test("System no-op needs no restart",delegate {var h=new FakeMachineHost();foreach(string id in MachineSettings.Ids)h.Values[id]=MachineSettings.Target(id);var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();Check(h.Writes==0&&s.Load().ChangeBoot==null,"Unnecessary writes/restart");e.Disable();Check(h.Writes==0,"No-op undo changed system");});
            Test("System restore retry after failure and restart",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();h.Fail="hags";Throws(e.Disable);Check(s.Load().Phase=="Restoring","Journal lost");h.Boot="boot-2";new MachineEngine(h,s,"owner").Disable();Originals(h);});
            Test("System external changes are preserved",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();h.Values["priority"]=24;e.Disable();Check(h.Values["priority"]==24,"External priority overwritten");});
            Test("Other user cannot restore machine profile",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();new MachineEngine(h,s,"owner").Enable();int writes=h.Writes;Throws(new MachineEngine(h,s,"other").Disable);Check(h.Writes==writes,"Cross-user restore");});
            Test("Other boot entry cannot restore machine profile",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();int writes=h.Writes;h.Entry="{33333333-3333-3333-3333-333333333333}";Throws(e.Disable);Check(h.Writes==writes,"Changed other loader");});
            Test("Silent system write rejection detected",delegate {var h=new FakeMachineHost {Ignore=true};var s=new MemoryMachineStore();Throws(new MachineEngine(h,s,"owner").Enable);Originals(h);});
            Test("Duplicate system enable preserves originals",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=new MachineEngine(h,s,"owner");e.Enable();Throws(e.Enable);Check(s.Load().Entries.First(x=>x.Id=="priority").Original==38,"Backup overwritten");});
            Test("System interrupted apply recovers",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var pending=new MachineState {Phase="Applying",OwnerSid="owner",BootEntry=h.Entry};foreach(string id in MachineSettings.Ids)pending.Entries.Add(new MachineEntry {Id=id,Original=h.Values[id]});pending.Entries[0].Attempted=true;h.Values["useplatformclock"]=null;s.Save(pending);new MachineEngine(h,s,"owner").Disable();Originals(h);});
            Test("Malformed system journal blocks operation",delegate {var s=new MemoryMachineStore();s.Save(new MachineState {Phase="Enabled",OwnerSid="owner",BootEntry="bad"});Throws(delegate {new MachineEngine(new FakeMachineHost(),s,"owner");});});
            Test("Elevation cancellation leaves normal profile untouched",delegate {var h=new FakeHost();using(var e=new Engine(h,new MemoryStore())) {var system=new TestSystemClient {CancelEnable=true};Throws(delegate {new ProfileController(e,system).Enable(ProfileOptions.Gaming());});Check(h.Writes==0&&e.State.Phase=="Disabled","Changed profile after UAC cancel");}});
            Test("Normal profile failure rolls back machine profile",delegate {var h=new FakeHost {Fail="create"};using(var e=new Engine(h,new MemoryStore())) {var system=new TestSystemClient();Throws(delegate {new ProfileController(e,system).Enable(ProfileOptions.Gaming());});Check(system.State.Phase=="Disabled"&&e.State.Phase=="Disabled","Composite rollback failed");}});
            Test("System restore failure still rolls back normal profile",delegate {using(var e=new Engine(new FakeHost(),new MemoryStore())) {var system=new TestSystemClient();var c=new ProfileController(e,system);c.Enable(ProfileOptions.Gaming());system.FailDisable=true;Throws(c.Disable);Check(e.State.Phase=="Disabled"&&system.State.Phase=="Enabled","Restore state incorrect");}});
            Test("Cancellation during Disable preserves both profiles",delegate {using(var e=new Engine(new FakeHost(),new MemoryStore())) {var system=new TestSystemClient();var c=new ProfileController(e,system);c.Enable(ProfileOptions.Gaming());system.CancelDisable=true;Throws(c.Disable);Check(e.State.Phase=="Enabled"&&system.State.Phase=="Enabled","Cancellation changed profile");}});
            return count;
        }
    }
}
