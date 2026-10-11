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
        public bool Block, Ignore;
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
        public bool CancelDisable,FailDisable,Pending;
        public int DisableCalls;
        public MachineState State {get{return Value;}}
        public bool RebootPending {get{return Pending;}}
        public void Disable(){DisableCalls++;if(CancelDisable)throw new OperationCanceledException();if(FailDisable)throw new IOException("System restore failed");Value.Phase="Disabled";}
    }
    public static class MachineTests {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Throws(Action action){bool failed=false;try{action();}catch{failed=true;}Check(failed,"Expected failure");}
        static void Test(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        static void Originals(FakeMachineHost h){Check(h.Values["useplatformclock"]==1&&h.Values["useplatformtick"]==null&&h.Values["disabledynamictick"]==0&&h.Values["hags"]==1&&h.Values["priority"]==38,"System originals not restored");}
        // Construct the persisted 0.5.6 state instead of enabling removed production code.
        static MachineEngine Legacy(FakeMachineHost h,MemoryMachineStore s) {
            var state=new MachineState {Phase="Enabled",OwnerSid="owner",BootEntry=h.Entry,ChangeBoot=h.Boot};
            foreach(string id in MachineSettings.Ids) {
                state.Entries.Add(new MachineEntry {Id=id,Original=h.Values[id],Attempted=true});
                h.Values[id]=MachineSettings.Target(id);
            }
            s.Save(state);return new MachineEngine(h,s,"owner");
        }
        public static int Run(){
            Test("New system enable is rejected with no writes or journal",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();Throws(new MachineEngine(h,s,"owner").Enable);Check(h.Writes==0&&s.Load().Phase=="Disabled","System changes still enabled");Originals(h);});
            Test("Legacy system profile exact BCD/HAGS/priority restoration",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();Legacy(h,s).Disable();Originals(h);Check(s.Load().Phase=="Disabled"&&s.Load().ChangeBoot=="boot-1","Missing restart state");});
            foreach(string id in MachineSettings.Ids){string point=id;Test("Legacy restore retry after partial failure: "+point,delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);h.Fail=point;Throws(e.Disable);Check(s.Load().Phase=="Restoring","Lost recovery state");new MachineEngine(h,s,"owner").Disable();Originals(h);});}
            Test("Legacy journal write failure blocks restore writes",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);s.Fail=true;Throws(e.Disable);Check(h.Writes==0,"Write without durable journal");});
            Test("BitLocker enabled later blocks BCD restore but restores other values",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);h.Block=true;Throws(e.Disable);Check(h.Values["hags"]==1&&h.Values["priority"]==38&&h.Values["useplatformtick"]==1&&s.Load().Phase=="Restoring","Unsafe BCD restore");h.Block=false;e.Disable();Originals(h);});
            Test("Legacy restore retry after restart",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);h.Fail="hags";Throws(e.Disable);h.Boot="boot-2";new MachineEngine(h,s,"owner").Disable();Originals(h);});
            Test("Legacy external changes are preserved",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);h.Values["priority"]=24;e.Disable();Check(h.Values["priority"]==24,"External priority overwritten");});
            Test("Other user cannot restore legacy profile",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();Legacy(h,s);Throws(new MachineEngine(h,s,"other").Disable);Check(h.Writes==0,"Cross-user restore");});
            Test("Other boot entry cannot restore legacy profile",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);h.Entry="{33333333-3333-3333-3333-333333333333}";Throws(e.Disable);Check(h.Writes==0,"Changed other loader");});
            Test("Silent legacy restore rejection keeps journal",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var e=Legacy(h,s);h.Ignore=true;Throws(e.Disable);Check(s.Load().Phase=="Restoring","Lost failed restoration");h.Ignore=false;new MachineEngine(h,s,"owner").Disable();Originals(h);});
            Test("Legacy interrupted apply recovers",delegate {var h=new FakeMachineHost();var s=new MemoryMachineStore();var pending=new MachineState {Phase="Applying",OwnerSid="owner",BootEntry=h.Entry};foreach(string id in MachineSettings.Ids)pending.Entries.Add(new MachineEntry {Id=id,Original=h.Values[id]});pending.Entries[0].Attempted=true;h.Values["useplatformclock"]=null;s.Save(pending);new MachineEngine(h,s,"owner").Disable();Originals(h);});
            Test("Malformed legacy journal blocks operation",delegate {var s=new MemoryMachineStore();s.Save(new MachineState {Phase="Enabled",OwnerSid="owner",BootEntry="bad"});Throws(delegate {new MachineEngine(new FakeMachineHost(),s,"owner");});});
            Test("Safe profile never requests machine changes and clones current plan",delegate {var h=new FakeHost();using(var e=new Engine(h,new MemoryStore())) {var system=new TestSystemClient {FailDisable=true};var c=new ProfileController(e,system);c.Enable(ProfileOptions.Gaming());Check(e.IsActive&&h.CopiedFrom==FakeHost.Original&&system.State.Phase=="Disabled"&&!h.Timer,"Unsafe default profile");c.Disable();Check(system.DisableCalls==0&&e.State.Phase=="Disabled","Safe restore touched machine client");}});
            Test("Legacy machine state blocks new profile without user writes",delegate {var h=new FakeHost();using(var e=new Engine(h,new MemoryStore())) {var system=new TestSystemClient();system.Value.Phase="Enabled";Throws(delegate {new ProfileController(e,system).Enable(ProfileOptions.Gaming());});Check(h.Writes==0&&system.DisableCalls==0,"Legacy overwritten");}});
            Test("Pending legacy reboot blocks new profile",delegate {var h=new FakeHost();using(var e=new Engine(h,new MemoryStore())) {Throws(delegate {new ProfileController(e,new TestSystemClient {Pending=true}).Enable(ProfileOptions.Gaming());});Check(h.Writes==0,"Applied before reboot");}});
            Test("Normal profile failure leaves machine untouched",delegate {var h=new FakeHost {Fail="create"};using(var e=new Engine(h,new MemoryStore())) {var system=new TestSystemClient {FailDisable=true};Throws(delegate {new ProfileController(e,system).Enable(ProfileOptions.Gaming());});Check(system.DisableCalls==0&&e.State.Phase=="Disabled","Failure touched machine profile");}});
            Test("Legacy system restore failure still rolls back user profile",delegate {using(var e=new Engine(new FakeHost(),new MemoryStore())) {e.Enable(ProfileOptions.Gaming());var system=new TestSystemClient {FailDisable=true};system.Value.Phase="Enabled";Throws(new ProfileController(e,system).Disable);Check(e.State.Phase=="Disabled"&&system.State.Phase=="Enabled","Restore state incorrect");}});
            Test("Cancellation during legacy restore preserves profiles",delegate {using(var e=new Engine(new FakeHost(),new MemoryStore())) {e.Enable(ProfileOptions.Gaming());var system=new TestSystemClient {CancelDisable=true};system.Value.Phase="Enabled";Throws(new ProfileController(e,system).Disable);Check(e.State.Phase=="Enabled"&&system.State.Phase=="Enabled","Cancellation changed profile");}});
            Test("Startup does not resume legacy timer",delegate {var h=new FakeHost();var s=new MemoryStore();using(var e=new Engine(h,s)){e.Enable(true);}var old=s.Load();old.SchemaVersion=2;s.Save(old);using(var e=new Engine(h,s)){new ProfileController(e,new TestSystemClient()).Resume();Check(!h.Timer,"Legacy timer resumed");e.Disable();}});
            Test("Startup blocks timer with pending machine migration",delegate {foreach(bool pending in new[]{false,true}) {var h=new FakeHost();var s=new MemoryStore();using(var e=new Engine(h,s)){e.Enable(true);}var system=new TestSystemClient {Pending=pending};if(!pending)system.Value.Phase="Enabled";using(var e=new Engine(h,s)){new ProfileController(e,system).Resume();Check(!h.Timer,"Timer resumed during migration");}}});
            Test("Safe opt-in timer resumes without machine writes",delegate {var h=new FakeHost();var s=new MemoryStore();using(var e=new Engine(h,s)){e.Enable(true);}using(var e=new Engine(h,s)){var system=new TestSystemClient();new ProfileController(e,system).Resume();Check(h.Timer&&system.DisableCalls==0,"Safe timer did not resume");}});
            Test("Safe profile state survives XML roundtrip",delegate {string dir=Path.Combine(Path.GetTempPath(),"OpenAntiLag-safe-"+Guid.NewGuid());try {var s=new XmlStateStore(Path.Combine(dir,"state.xml"));var h=new FakeHost();using(var e=new Engine(h,s)){new ProfileController(e,new TestSystemClient()).Enable(ProfileOptions.Gaming());}using(var e=new Engine(h,s)){Check(e.State.SchemaVersion==3&&e.IsActive,"Safe schema unreadable");new ProfileController(e,new TestSystemClient()).Disable();Check(h.Current==FakeHost.Original,"Restore failed");}}finally {if(Directory.Exists(dir))Directory.Delete(dir,true);}});
            return count;
        }
    }
}