using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
namespace OpenAntiLag {
    public sealed class FakeAmd:IAmdDriver {
        public Dictionary<string,int> Values=new Dictionary<string,int>{{"antilag",0},{"chill",1},{"boost",0},{"enhanced",1},{"morphological",1},{"vsync",3}};
        public int Writes,FailAt;public bool Ignore;public string Selected;
        public List<AmdGpu> Devices {get{return new List<AmdGpu>{new AmdGpu {Id="GPU-A",Name="Test Radeon"}};}}
        public void Select(string id){if(id!="GPU-A")throw new InvalidOperationException("Wrong GPU");Selected=id;}
        public int? Read(string key){int v;return Values.TryGetValue(key,out v)?(int?)v:null;}
        public void Write(string key,int value){Writes++;if(Writes==FailAt)throw new IOException("Write failed");if(Ignore)return;Values[key]=value;if(value==1&&(key=="antilag"||key=="chill"||key=="boost"))foreach(var k in new[]{"antilag","chill","boost"})if(k!=key&&Values.ContainsKey(k))Values[k]=0;}
        public void Dispose(){}
    }
    public sealed class MemoryAmd:IAmdStore {
        AmdBackup state;public bool Fail;
        static AmdBackup Copy(AmdBackup b){if(b==null)return null;var s=new XmlSerializer(typeof(AmdBackup));using(var m=new MemoryStream()){s.Serialize(m,b);m.Position=0;return (AmdBackup)s.Deserialize(m);}}
        public AmdBackup Load(){return Copy(state);}public void Save(AmdBackup b){if(Fail)throw new IOException("Disk failed");state=Copy(b);}public void Delete(){state=null;}
    }
    public static class AmdTests {
        static void Assert(bool x,string m){if(!x)throw new Exception(m);}static void Throws(Action a){bool failed=false;try{a();}catch{failed=true;}Assert(failed,"Expected failure");}
        public static int Run(){int n=0;Action<string,Action> test=(name,a)=>{a();n++;Console.WriteLine("PASS AMD "+name);};
            test("Apply and restore conflicting feature group after restart",()=>{var d=new FakeAmd();var original=new Dictionary<string,int>(d.Values);var s=new MemoryAmd();new AmdProfiles(()=>d,s).Apply("GPU-A",true,false);Assert(d.Values["antilag"]==1&&d.Values["chill"]==0,"Not applied");new AmdProfiles(()=>d,s).Restore();Assert(original.All(p=>d.Values[p.Key]==p.Value)&&s.Load()==null,"Bad restore");});
            test("Backup failure causes no writes",()=>{var d=new FakeAmd();var s=new MemoryAmd{Fail=true};Throws(()=>new AmdProfiles(()=>d,s).Apply("GPU-A",true,false));Assert(d.Writes==0,"Write without backup");});
            test("Partial immediate write restored",()=>{var d=new FakeAmd{FailAt=3};var s=new MemoryAmd();var c=new AmdProfiles(()=>d,s);Throws(()=>c.Apply("GPU-A",true,false));Assert(s.Load()!=null,"Lost journal");d.FailAt=0;c.Restore();Assert(d.Values["chill"]==1&&d.Values["antilag"]==0,"Partial apply leaked");});
            test("Restore interruption inside group is recoverable",()=>{var d=new FakeAmd();var s=new MemoryAmd();var c=new AmdProfiles(()=>d,s);c.Apply("GPU-A",true,false);d.FailAt=d.Writes+2;Throws(()=>c.Restore());Assert(s.Load()!=null,"Lost journal");d.FailAt=0;c.Restore();Assert(d.Values["chill"]==1&&s.Load()==null,"Retry failed");});
            test("External group preserved together",()=>{var d=new FakeAmd();var s=new MemoryAmd();var c=new AmdProfiles(()=>d,s);c.Apply("GPU-A",true,false);d.Write("boost",1);c.Restore();Assert(d.Values["boost"]==1&&d.Values["chill"]==0,"External group overwritten");});
            test("Unsupported features are skipped",()=>{var d=new FakeAmd();d.Values.Remove("morphological");var s=new MemoryAmd();var c=new AmdProfiles(()=>d,s);Assert(c.Inspect("GPU-A").Contains("не поддерживается"),"Not reported");c.Apply("GPU-A",true,false);Assert(!d.Values.ContainsKey("morphological"),"Unsupported written");c.Restore();});
            test("Silent rejection keeps recovery copy",()=>{var d=new FakeAmd{Ignore=true};var s=new MemoryAmd();Throws(()=>new AmdProfiles(()=>d,s).Apply("GPU-A",true,false));Assert(s.Load()!=null,"False success");});
            test("Repeat apply and wrong GPU refused",()=>{var d=new FakeAmd();var s=new MemoryAmd();var c=new AmdProfiles(()=>d,s);Throws(()=>c.Apply("GPU-B",true,false));Assert(d.Writes==0,"Wrong GPU modified");c.Apply("GPU-A",true,false);int w=d.Writes;Throws(()=>c.Apply("GPU-A",false,true));Assert(w==d.Writes,"Original replaced");});
            test("XML and foreign backup validation",()=>{string dir=Path.Combine(Path.GetTempPath(),"OpenAntiLag-amd-"+Guid.NewGuid());Directory.CreateDirectory(dir);try{var store=new AmdStore(Path.Combine(dir,"state.xml"));var b=new AmdBackup{Gpu="GPU-A",Entries=new List<AmdEntry>{new AmdEntry{Key="vsync",Original=3,Target=0}}};store.Save(b);Assert(store.Load().Entries[0].Original==3,"Roundtrip");b.Entries[0].Target=99;Throws(()=>store.Save(b));b.Entries[0].Target=0;b.Machine="other";Throws(()=>store.Save(b));}finally{Directory.Delete(dir,true);}});
            return n;
        }
    }
}
