using System;
using System.IO;
using System.Web.Script.Serialization;

namespace OpenAntiLag {
    public static class UpdateTests {
        static int count;
        static void Assert(bool ok) {if(!ok)throw new Exception("Update assertion failed");}
        static void Test(string name,Action action){action();count++;Console.WriteLine("PASS "+name);}
        static void Reject(Action action){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected);}
        static ReleaseInfo Release() {return new ReleaseInfo {tag_name="v0.6.0",assets=new[]{new ReleaseAsset {name="OpenAntiLag.exe",size=100,digest="sha256:"+new string('a',64),browser_download_url=Updates.Repository+"/releases/download/v0.6.0/OpenAntiLag.exe"}}};}
        static ReleaseAsset Select(ReleaseInfo release){Version version;return Updates.Select(new JavaScriptSerializer().Serialize(release),Updates.Current,out version);}
        static void Files(Action<string,string> action) {
            string dir=Path.Combine(Path.GetTempPath(),"OpenAntiLag-update-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try {string old=Path.Combine(dir,"old.exe"),next=Path.Combine(dir,"next.exe");File.WriteAllText(old,"old executable");File.WriteAllText(next,"new executable");action(old,next);}
            finally {Directory.Delete(dir,true);}
        }
        public static int Run() {
            Test("Update accepts complete newer stable release",delegate{Assert(Select(Release())!=null);});
            Test("Update skips current and older versions",delegate {var r=Release();r.tag_name="v0.5.0";Assert(Select(r)==null);r.tag_name="v0.3.0";Assert(Select(r)==null);});
            Test("Update skips beta and drafts",delegate {var r=Release();r.prerelease=true;Assert(Select(r)==null);r.prerelease=false;r.draft=true;Assert(Select(r)==null);r.draft=false;r.tag_name="v0.6.0-beta.1";Assert(Select(r)==null);});
            Test("Update rejects foreign URL",delegate {var r=Release();r.assets[0].browser_download_url="https://example.org/OpenAntiLag.exe";Reject(delegate{Select(r);});});
            Test("Update rejects missing checksum",delegate {var r=Release();r.assets[0].digest=null;Reject(delegate{Select(r);});});
            Test("Update rejects missing executable",delegate {var r=Release();r.assets=new ReleaseAsset[0];Reject(delegate{Select(r);});});
            Test("Update rejects ambiguous duplicate asset",delegate {var r=Release();r.assets=new[]{r.assets[0],r.assets[0]};Reject(delegate{Select(r);});});
            Test("Update rejects excessive size",delegate {var r=Release();r.assets[0].size=Int64.MaxValue;Reject(delegate{Select(r);});});
            Test("Update rejects malformed response",delegate {Version version;Reject(delegate {Updates.Select("invalid",Updates.Current,out version);});});
            Test("Update replaces executable and keeps backup",delegate {Files(delegate(string old,string next){bool launched=false;Updates.Replace(next,old,Updates.Hash(old),Updates.Hash(next),delegate(string path){launched=true;Assert(path==old);});Assert(launched&&File.ReadAllText(old)=="new executable"&&File.ReadAllText(old+".previous")=="old executable");});});
            Test("Update restores executable if restart fails",delegate {Files(delegate(string old,string next){Reject(delegate {Updates.Replace(next,old,Updates.Hash(old),Updates.Hash(next),delegate{throw new IOException("launch failed");});});Assert(File.ReadAllText(old)=="old executable");});});
            Test("Update refuses tampered download",delegate {Files(delegate(string old,string next){Reject(delegate {Updates.Replace(next,old,Updates.Hash(old),new string('a',64),delegate{});});Assert(File.ReadAllText(old)=="old executable"&&!File.Exists(old+".previous"));});});
            Test("Update refuses changed installed executable",delegate {Files(delegate(string old,string next){Reject(delegate {Updates.Replace(next,old,new string('a',64),Updates.Hash(next),delegate{});});Assert(File.ReadAllText(old)=="old executable");});});
            Test("Update leaves locked executable unchanged",delegate {Files(delegate(string old,string next){using(var handle=new FileStream(old,FileMode.Open,FileAccess.Read,FileShare.Read)){Reject(delegate {Updates.Replace(next,old,Updates.Hash(old),Updates.Hash(next),delegate{});});}Assert(File.ReadAllText(old)=="old executable");});});
            return count;
        }
    }
}
