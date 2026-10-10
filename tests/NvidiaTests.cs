using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace OpenAntiLag {
    public sealed class FakeNvidiaDevice {
        public readonly Dictionary<uint,uint> Defaults=new Dictionary<uint,uint>();
        public Dictionary<uint,uint> Overrides=new Dictionary<uint,uint>();
        public int Writes,Saves,FailWriteAt;
        public bool FailSave,PartialSave,IgnoreSave;
        public FakeNvidiaDevice() {foreach(var s in NvidiaPreset.Build(false,0,false,false))if(!s.AllowAbsent)Defaults[s.Id]=77;}
        public INvidiaDriver Open() {return new Session(this);}
        sealed class Session:INvidiaDriver {
            readonly FakeNvidiaDevice d;
            readonly Dictionary<uint,uint> pending;
            public Session(FakeNvidiaDevice d) {this.d=d;pending=new Dictionary<uint,uint>(d.Overrides);}
            public string Version {get{return "617.42";}}
            public NvidiaValue Read(uint id) {uint v;if(pending.TryGetValue(id,out v))return new NvidiaValue {Value=v,UserOverride=true};if(d.Defaults.TryGetValue(id,out v))return new NvidiaValue {Value=v};return null;}
            public void Write(uint id,uint value) {d.Writes++;if(d.Writes==d.FailWriteAt)throw new IOException("Injected staging failure");pending[id]=value;}
            public void Reset(uint id) {pending.Remove(id);}
            public void Save() {d.Saves++;if(d.PartialSave){d.Overrides[pending.First().Key]=pending.First().Value;d.PartialSave=false;throw new IOException("Partial save");}if(d.FailSave)throw new IOException("Save failure");if(!d.IgnoreSave)d.Overrides=new Dictionary<uint,uint>(pending);}
            public void Dispose() {}
        }
    }
    public sealed class MemoryNvidiaStore:INvidiaStore {
        public NvidiaBackup State;
        public bool FailSave;
        static NvidiaBackup Copy(NvidiaBackup b) {if(b==null)return null;var x=new XmlSerializer(typeof(NvidiaBackup));using(var m=new MemoryStream()){x.Serialize(m,b);m.Position=0;return (NvidiaBackup)x.Deserialize(m);}}
        public NvidiaBackup Load() {return Copy(State);}
        public void Save(NvidiaBackup b) {if(FailSave)throw new IOException("Disk failure");State=Copy(b);}
        public void Delete(){State=null;}
    }
    public static class NvidiaTests {
        static void Assert(bool value,string text){if(!value)throw new Exception(text);}
        static void Throws(Action a){bool failed=false;try{a();}catch{failed=true;}Assert(failed,"Expected failure");}
        public static int Run() {
            int count=0;
            Action<string,Action> test=(name,action)=>{action();count++;Console.WriteLine("PASS NVIDIA "+name);};
            Func<List<NvidiaOption>> preset=()=>NvidiaPreset.Build(false,0,false,false);
            test("Apply, restart, exact override and inheritance restoration",()=>{
                var d=new FakeNvidiaDevice();d.Overrides[0x1057EB71]=1;var original=new Dictionary<uint,uint>(d.Overrides);var s=new MemoryNvidiaStore();
                new NvidiaController(d.Open,s).Apply(preset());Assert(s.State.Phase=="Applied","Not applied");
                foreach(var e in preset())using(var session=d.Open())Assert(session.Read(e.Id).Value==e.Value,"Wrong value");
                new NvidiaController(d.Open,s).Restore();Assert(s.State==null&&d.Overrides.Count==original.Count&&d.Overrides[0x1057EB71]==1,"Original overrides changed");
            });
            test("Journal failure prevents writes",()=>{var d=new FakeNvidiaDevice();var s=new MemoryNvidiaStore{FailSave=true};Throws(()=>new NvidiaController(d.Open,s).Apply(preset()));Assert(d.Writes==0&&d.Saves==0,"Write before durable journal");});
            test("Repeated apply does not overwrite original",()=>{var d=new FakeNvidiaDevice();var s=new MemoryNvidiaStore();var c=new NvidiaController(d.Open,s);c.Apply(preset());int writes=d.Writes;Throws(()=>c.Apply(NvidiaPreset.Build(true,141,true,true)));Assert(d.Writes==writes&&s.State.Entries[0].Original==77,"Backup overwritten");});
            test("Staging failure leaves database unchanged and recovery works",()=>{var d=new FakeNvidiaDevice{FailWriteAt=3};var s=new MemoryNvidiaStore();var c=new NvidiaController(d.Open,s);Throws(()=>c.Apply(preset()));Assert(d.Overrides.Count==0&&d.Saves==0&&s.State!=null,"Staged changes persisted");c.Restore();Assert(s.State==null&&d.Overrides.Count==0,"Recovery failed");});
            test("Partial save can be recovered after restart",()=>{var d=new FakeNvidiaDevice{PartialSave=true};var s=new MemoryNvidiaStore();Throws(()=>new NvidiaController(d.Open,s).Apply(preset()));Assert(s.State!=null&&d.Overrides.Count==1,"No partial save");new NvidiaController(d.Open,s).Restore();Assert(d.Overrides.Count==0&&s.State==null,"Partial changes leaked");});
            test("Silent save rejection is detected",()=>{var d=new FakeNvidiaDevice{IgnoreSave=true};var s=new MemoryNvidiaStore();Throws(()=>new NvidiaController(d.Open,s).Apply(preset()));Assert(s.State.Phase=="Pending","False success");});
            test("External changes preserved",()=>{var d=new FakeNvidiaDevice();var s=new MemoryNvidiaStore();var c=new NvidiaController(d.Open,s);c.Apply(preset());d.Overrides[0x1057EB71]=3;c.Restore();Assert(d.Overrides.Count==1&&d.Overrides[0x1057EB71]==3,"External choice overwritten");});
            test("Restore save failure retains journal for retry",()=>{var d=new FakeNvidiaDevice();var s=new MemoryNvidiaStore();var c=new NvidiaController(d.Open,s);c.Apply(preset());d.FailSave=true;Throws(()=>c.Restore());Assert(s.State!=null,"Journal lost");d.FailSave=false;c.Restore();Assert(d.Overrides.Count==0,"Retry failed");});
            test("Unsupported public setting aborts before write",()=>{var d=new FakeNvidiaDevice();d.Defaults.Remove(0x1057EB71);var s=new MemoryNvidiaStore();Throws(()=>new NvidiaController(d.Open,s).Apply(preset()));Assert(d.Writes==0&&s.State==null,"Unsupported write");});
            test("No-op does not create backup",()=>{var d=new FakeNvidiaDevice();foreach(var o in preset())d.Overrides[o.Id]=o.Value;var s=new MemoryNvidiaStore();new NvidiaController(d.Open,s).Apply(preset());Assert(d.Writes==0&&s.State==null,"No-op modified state");});
            test("Driver default update keeps inheritance",()=>{var d=new FakeNvidiaDevice();var s=new MemoryNvidiaStore();var c=new NvidiaController(d.Open,s);c.Apply(preset());d.Defaults[0x1057EB71]=9;c.Restore();Assert(!d.Overrides.ContainsKey(0x1057EB71),"Default pinned");});
            test("XML roundtrip and corrupt/foreign/unknown journal rejected",()=>{
                string dir=Path.Combine(Path.GetTempPath(),"OpenAntiLag-nvidia-"+Guid.NewGuid());Directory.CreateDirectory(dir);
                try {string path=Path.Combine(dir,"state.xml");var s=new NvidiaStore(path);var b=new NvidiaBackup {Entries=new List<NvidiaBackupEntry>{new NvidiaBackupEntry {Id=0x1057EB71,Original=5,Applied=1,Existed=true}}};s.Save(b);Assert(s.Load().Entries[0].Original==5,"Backup roundtrip");b.Machine="other";Throws(()=>s.Save(b));b.Machine=Environment.MachineName;b.Entries[0].Id=123;Throws(()=>s.Save(b));File.WriteAllText(path,"broken");Throws(()=>s.Load());}finally {Directory.Delete(dir,true);}
            });
            test("VRR limits validated",()=>{Throws(()=>NvidiaPreset.Build(true,0,false,false));Throws(()=>NvidiaPreset.Build(true,1001,false,false));Assert(NvidiaPreset.Build(true,237,true,true).First(x=>x.Id==0x10835002).Value==237,"Wrong FPS");});
            return count;
        }
    }
}
