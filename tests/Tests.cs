using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Linq;
using System.Threading.Tasks;

namespace OpenAntiLag {
    public sealed class FakeHost : IHost {
        public const string Original = "11111111-1111-1111-1111-111111111111";
        public string Current = Original;
        public readonly HashSet<string> Plans = new HashSet<string> { Original };
        public string Fail;
        public bool Timer;
        public int Writes;
        public readonly Dictionary<string, int[]> Values = new Dictionary<string, int[]> { { "game-mode", new [] { 0 } }, { "capture-app", new [] { 1 } }, { "mouse-acceleration", new [] { 6, 10, 1 } } };
        public bool IgnoreWrite;
        public string ActivePlan() { return Current; }
        public bool HasPlan(string id) { return Plans.Contains(id); }
        void Check(string action) { if (Fail == action) { Fail = null; throw new IOException("Injected failure: " + action); } }
        public void CreatePlan(string id) { Writes++; Plans.Add(id); Check("create"); }
        public void Activate(string id) { Check("activate"); if (!Plans.Contains(id)) throw new IOException("Missing plan"); Writes++; Current = id; }
        public void DeletePlan(string id) { Check("delete"); if (Current == id) throw new IOException("Active plan"); Writes++; Plans.Remove(id); }
        public void StartTimer() { Check("timer"); Timer = true; }
        public void StopTimer() { Timer = false; }
        public void ConfigurePlan(string id, ProfileOptions options) { Check("configure"); if (options.CpuReady || options.PcieReady) Writes++; }
        public int[] ReadSetting(string id) { int[] value; return Values.TryGetValue(id, out value) ? (int[])value.Clone() : null; }
        public void WriteSetting(string id, int[] value) { Writes++; if (IgnoreWrite) return; if (value == null) Values.Remove(id); else Values[id] = (int[])value.Clone(); Check("write-" + id); }
    }
    public sealed class MemoryStore : IStateStore {
        ProfileState state = new ProfileState();
        public bool FailSave;
        static ProfileState Copy(ProfileState s) { var serializer = new XmlSerializer(typeof(ProfileState)); using (var stream = new MemoryStream()) { serializer.Serialize(stream, s); stream.Position = 0; return (ProfileState)serializer.Deserialize(stream); } }
        public ProfileState Load() { return Copy(state); }
        public void Save(ProfileState s) { if (FailSave) throw new IOException("Disk unavailable"); state = Copy(s); }
    }
    public static class Tests {
        static int count;
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Throws(Action action) { bool threw = false; try { action(); } catch { threw = true; } Assert(threw, "Expected failure"); }
        static void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); count++; }
        [STAThread] public static int Main(string[] args) {
            if(args.Length==1&&args[0]=="--amd-read") {try {using(var d=new AmdDriver()){foreach(var gpu in d.Devices){Console.WriteLine(gpu.Name);d.Select(gpu.Id);foreach(var k in AmdProfiles.Keys)Console.WriteLine(k+": "+d.Read(k));}}return 0;}catch(NotSupportedException ex){Console.WriteLine(ex.Message);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
            if(args.Length==1&&args[0]=="--check-elevation") {try {ElevatedProcess.Run(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"OpenAntiLag.exe"),"--machine check "+MachineWorker.Sid);Console.WriteLine("PASS actual UAC launch; no system settings changed.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
            if(args.Length==1&&args[0]=="--nvidia-stage-check") {
                try {
                    var plan=NvidiaPreset.Build(false,0,false,false);var before=new Dictionary<uint,NvidiaValue>();
                    using(var d=new NvidiaDriver()) {
                        foreach(var s in plan)before[s.Id]=d.Read(s.Id);
                        foreach(var s in plan)d.Write(s.Id,s.Value);
                        foreach(var s in plan)Assert(d.Read(s.Id).Value==s.Value,"Staged value mismatch");
                        foreach(var s in plan) {var old=before[s.Id];if(old!=null&&old.UserOverride)d.Write(s.Id,old.Value);else d.Reset(s.Id);}
                        foreach(var s in plan) {var a=before[s.Id];var b=d.Read(s.Id);Assert(a==null?b==null:b!=null&&a.Value==b.Value&&a.UserOverride==b.UserOverride,"Staged restoration mismatch");}
                        // Intentionally NO Save: session-only compatibility test.
                    }
                    using(var d=new NvidiaDriver())foreach(var s in plan) {var a=before[s.Id];var b=d.Read(s.Id);Assert(a==null?b==null:b!=null&&a.Value==b.Value&&a.UserOverride==b.UserOverride,"Persistent state changed");}
                    Console.WriteLine("PASS NVIDIA 25 settings staged and restored in disposable session; persistent values unchanged.");return 0;
                }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            }
            if(args.Length==1&&args[0]=="--nvidia-read") { try { Console.WriteLine(new NvidiaController(()=>new NvidiaDriver(),new NvidiaStore(Path.Combine(Program.DataDirectory,"nvidia-backup.xml"))).Inspect(NvidiaPreset.Build(false,0,false,false))); return 0; } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;} }
            if(args.Length==2&&args[0]=="--export-gpu") { Directory.CreateDirectory(args[1]); foreach(bool amd in new[]{false,true}) foreach(bool smooth in new[]{false,true}) File.WriteAllText(Path.Combine(args[1],(amd?"AMD":"NVIDIA-617.42")+(smooth?"-VRR":"-Esports")+".txt"),GpuConfigs.Guide(amd,smooth),new System.Text.UTF8Encoding(true)); return 0; }
            if(args.Length==1&&args[0]=="--fetch-latest") {try {var package=Updates.Download(new Version(0,0,0,0)).GetAwaiter().GetResult();Console.WriteLine(package==null?"No release":"Verified GitHub download: "+package.Version+" SHA256 "+package.Hash);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
            if(args.Length == 1 && args[0] == "--ui-check") return UiCheck();
            if (args.Length == 1 && args[0] == "--inspect") {
                try { var host = new WindowsHost(); Console.WriteLine("Read-only Windows adapter check"); Console.WriteLine("Power plan: " + host.ActivePlan()); foreach (string id in Settings.Selected(ProfileOptions.Gaming())) { var value = host.ReadSetting(id); Console.WriteLine(id + ": " + (value == null ? "absent" : String.Join(",", value))); } return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length >= 2 && args[0] == "--preview") {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var e = new Engine(new FakeHost(), new MemoryStore())) {
                if(args.Length > 2 && args[2] == "active") e.Enable(new ProfileOptions { GameMode=true, DisableCapture=true, DisableMouseAcceleration=true, CpuReady=true, PcieReady=true, Timer=true });
                using (var f = new MainForm(e, false, true)) {
                    if(args.Length > 2 && args[2] == "small") f.ClientSize = new Size(630,560);
                    if(args.Length > 3) {
                        float scale=float.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture);
                        var fonts=new Dictionary<Control,Font>(); CaptureFonts(f,fonts);
                        f.SuspendLayout(); f.Scale(new SizeF(scale,scale));
                        foreach(var pair in fonts) pair.Key.Font=new Font(pair.Value.FontFamily,pair.Value.Size*scale,pair.Value.Style);
                        f.ResumeLayout(true);
                    }
                    if(args.Length>2 && (args[2]=="nvidia" || args[2]=="amd")) ((TabControl)f.Controls.Find("mainTabs",true)[0]).SelectedIndex=args[2]=="amd"?2:1;
                    var tick = new System.Windows.Forms.Timer { Interval = 600 };
                    tick.Tick += delegate {
                        tick.Stop();
                        Console.WriteLine(WindowTheme.Inspect(f.Handle));
                        using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height)) { f.Controls[0].DrawToBitmap(bmp, new Rectangle(Point.Empty, f.ClientSize)); bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                        CheckLayout(f);
                        f.Close();
                    };
                    f.Shown += delegate { tick.Start(); }; Application.Run(f); tick.Dispose();
                } }
                return 0;
            }
            try {
                Test("Enable and exact rollback", delegate {
                    var h = new FakeHost(); var s = new MemoryStore(); using (var e = new Engine(h,s)) { e.Enable(true); Assert(e.IsActive && h.Timer, "Not active"); Assert(s.Load().OriginalPlan == FakeHost.Original, "Backup lost"); e.Disable(); Assert(h.Current == FakeHost.Original && h.Plans.Count == 1 && !h.Timer && s.Load().Phase == "Disabled", "Rollback mismatch"); }
                });
                Test("Duplicate enable never overwrites backup", delegate { var h = new FakeHost(); var s = new MemoryStore(); using (var e = new Engine(h,s)) { e.Enable(false); Throws(delegate { e.Enable(false); }); Assert(s.Load().OriginalPlan == FakeHost.Original, "Backup overwritten"); } });
                foreach (string failure in new [] { "create", "activate", "timer" }) {
                    string point = failure;
                    Test("Rollback after " + point + " failure", delegate { var h = new FakeHost { Fail = point }; var s = new MemoryStore(); using(var e = new Engine(h,s)) { Throws(delegate { e.Enable(true); }); Assert(h.Current == FakeHost.Original && h.Plans.Count == 1 && s.Load().Phase == "Disabled", "Incomplete rollback"); } });
                }
                Test("Disk failure before apply has zero system writes", delegate { var h = new FakeHost(); var s = new MemoryStore { FailSave = true }; using(var e = new Engine(h,s)) { Throws(delegate { e.Enable(false); }); Assert(h.Writes == 0, "Changed system without recovery record"); } });
                Test("Recover after process interruption", delegate { var h = new FakeHost(); var s = new MemoryStore(); string owned = Guid.NewGuid().ToString(); s.Save(new ProfileState { Phase = "Applying", OriginalPlan = FakeHost.Original, OwnedPlan = owned }); h.CreatePlan(owned); h.Activate(owned); using(var e = new Engine(h,s)) { e.Disable(); Assert(h.Current == FakeHost.Original && h.Plans.Count == 1, "Recovery failed"); } });
                Test("External plan selection preserved", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(false); string other = Guid.NewGuid().ToString(); h.Plans.Add(other); h.Activate(other); Assert(!e.IsActive, "Wrong status"); e.Disable(); Assert(h.Current == other && h.Plans.Count == 2, "External plan changed"); } });
                Test("Retry failed restoration", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(false); h.Fail = "delete"; Throws(delegate { e.Disable(); }); Assert(s.Load().Phase == "Restoring", "Journal lost"); } using(var e = new Engine(h,s)) { e.Disable(); Assert(h.Plans.Count == 1 && s.Load().Phase == "Disabled", "Retry failed"); } });
                Test("Missing original plan retains recovery state", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(false); h.Plans.Remove(FakeHost.Original); Throws(delegate { e.Disable(); }); Assert(s.Load().Phase == "Restoring" && h.Plans.Contains(e.State.OwnedPlan), "Recovery data destroyed"); } });
                Test("Startup resumes timer without rewriting power plans", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(true); } int writes = h.Writes; using(var e = new Engine(h,s)) { e.Resume(); Assert(h.Timer && h.Writes == writes, "Unexpected startup changes"); } Assert(!h.Timer, "Timer leaked"); });
                Test("Startup respects external plan change", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(true); } h.Activate(FakeHost.Original); using(var e = new Engine(h,s)) { e.Resume(); Assert(!h.Timer && !e.IsActive, "Wrong resumed status"); } });
                Test("Atomic state roundtrip and corrupt state rejection", delegate {
                    string dir = Path.Combine(Path.GetTempPath(), "OpenAntiLag-test-" + Guid.NewGuid());
                    try { var store = new XmlStateStore(Path.Combine(dir,"state.xml")); store.Save(new ProfileState()); store.Save(new ProfileState { Phase = "Enabled", OriginalPlan = FakeHost.Original, OwnedPlan = Guid.NewGuid().ToString() }); Assert(store.Load().Phase == "Enabled", "Save failed"); File.WriteAllText(Path.Combine(dir,"state.xml"), "broken XML"); Throws(delegate { store.Load(); }); }
                    finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); }
                });
                ProfileTests(); count += MachineTests.Run(); count += UpdateTests.Run(); count += NvidiaTests.Run();count += AmdTests.Run();                Test("UAC launches on STA with exact path and working directory",delegate {ElevatedProcess.Run(System.Windows.Forms.Application.ExecutablePath,"--machine check test",delegate(System.Diagnostics.ProcessStartInfo info){Assert(System.Threading.Thread.CurrentThread.GetApartmentState()==System.Threading.ApartmentState.STA,"Not STA");Assert(info.Verb=="runas"&&info.UseShellExecute&&info.WorkingDirectory==Path.GetDirectoryName(info.FileName)&&info.Arguments=="--machine check test","Wrong UAC request");return 0;});});
                Test("UAC cancellation remains cancellation",delegate {try {ElevatedProcess.Run(System.Windows.Forms.Application.ExecutablePath,"test",delegate {throw new System.ComponentModel.Win32Exception(1223);});throw new Exception("Expected cancellation");}catch(OperationCanceledException){}});
                Test("UAC unknown native error is actionable",delegate {try {ElevatedProcess.Run(System.Windows.Forms.Application.ExecutablePath,"test",delegate {throw new System.ComponentModel.Win32Exception(-2);});throw new Exception("Expected launch failure");}catch(IOException ex){Assert(ex.Message.Contains("0xFFFFFFFE")&&ex.InnerException is System.ComponentModel.Win32Exception,"Lost diagnostic");}});
                Console.WriteLine(count + " tests passed. No real system settings changed."); return 0;
            } catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        static void CaptureFonts(Control c,Dictionary<Control,Font> fonts) { fonts.Add(c,c.Font); foreach(Control child in c.Controls)CaptureFonts(child,fonts); }
        static async Task WaitUntil(Func<bool> ready) { for(int i=0;i<200;i++) { if(ready())return; await Task.Delay(25); } throw new Exception("UI transition timed out"); }
        static int UiCheck() {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); int result=0;
            using(var engine=new Engine(new FakeHost(),new MemoryStore()))
            using(var form=new MainForm(engine,false,true)) {
                var tick=new System.Windows.Forms.Timer { Interval=800 };
                tick.Tick+=async delegate {
                    tick.Stop();
                    try {
                        var on=(Button)form.Controls.Find("enable",true)[0]; var off=(Button)form.Controls.Find("disable",true)[0];
                        await WaitUntil(delegate { return on.Enabled; });
                        CheckLayout(form); on.PerformClick();
                        await WaitUntil(delegate { return engine.State.Phase=="Enabled" && off.Enabled; });
                        Assert(!on.Enabled,"Enable should be disabled when active"); CheckLayout(form); off.PerformClick();
                        await WaitUntil(delegate { return engine.State.Phase=="Disabled" && on.Enabled; });
                        form.ClientSize=new Size(630,560); form.PerformLayout(); CheckLayout(form);
                        Assert(on.Bottom<=on.Parent.ClientSize.Height && off.Enabled==false,"Action state/layout invalid");
                        var tabs=(TabControl)form.Controls.Find("mainTabs",true)[0];
                        for(int i=1;i<3;i++) { tabs.SelectedIndex=i; form.PerformLayout(); var mode=(ComboBox)tabs.TabPages[i].Controls.Find("gpuMode",true)[0]; var guide=(TextBox)tabs.TabPages[i].Controls.Find("gpuGuide",true)[0]; mode.SelectedIndex=1; Assert(guide.Text.Contains("РЕЖИМ: БЕЗ РАЗРЫВОВ"),"VRR guide did not change"); mode.SelectedIndex=0; Assert(guide.Text.Contains("РЕЖИМ: МИНИМАЛЬНАЯ ЗАДЕРЖКА"),"Esports guide did not change"); Assert(guide.Height>100 && guide.Width>400,"Guide not readable"); }
                        var gpuDevice=new FakeNvidiaDevice();var gpuStore=new MemoryNvidiaStore();bool locked=false;
                        var gpuPage=new NvidiaPage(false,delegate {if(locked)return false;locked=true;return true;},delegate {locked=false;},new NvidiaController(gpuDevice.Open,gpuStore));
                        tabs.TabPages.Add(gpuPage);tabs.SelectedTab=gpuPage;
                        var gpuApply=(Button)gpuPage.Controls.Find("applyNvidia",true)[0];var gpuRestore=(Button)gpuPage.Controls.Find("restoreNvidia",true)[0];
                        await WaitUntil(()=>gpuApply.Enabled&&!locked);gpuApply.PerformClick();await WaitUntil(()=>gpuApply.Enabled&&!locked);
                        Assert(gpuStore.State!=null&&gpuStore.State.Phase=="Applied","UI GPU apply failed");
                        gpuRestore.PerformClick();await WaitUntil(()=>gpuRestore.Enabled&&!locked);Assert(gpuStore.State==null&&gpuDevice.Overrides.Count==0,"UI GPU restore failed");
                        tabs.TabPages.Remove(gpuPage);gpuPage.Dispose();
                        var amdDriver=new FakeAmd();var amdStore=new MemoryAmd();var amdPage=new AmdPage(false,()=>true,()=>{},new AmdProfiles(()=>amdDriver,amdStore));tabs.TabPages.Add(amdPage);tabs.SelectedTab=amdPage;
                        var amdApply=(Button)amdPage.Controls.Find("applyAmd",true)[0];var amdRestore=(Button)amdPage.Controls.Find("restoreAmd",true)[0];
                        await WaitUntil(()=>amdApply.Enabled);amdApply.PerformClick();await WaitUntil(()=>amdApply.Enabled);Assert(amdStore.Load()!=null&&amdStore.Load().Phase=="Applied","AMD UI apply failed");
                        amdRestore.PerformClick();await WaitUntil(()=>amdRestore.Enabled);Assert(amdStore.Load()==null&&amdDriver.Values["chill"]==1,"AMD UI restore failed");tabs.TabPages.Remove(amdPage);amdPage.Dispose();
                        Console.WriteLine("PASS UI: system actions, GPU tabs, NVIDIA apply/restore buttons on simulated driver, narrow layout");
                    } catch(Exception ex) { Console.Error.WriteLine(ex); result=1; }
                    form.Close();
                };
                form.Shown+=delegate { tick.Start(); }; Application.Run(form); tick.Dispose();
            }
            return result;
        }
        static void CheckLayout(Control root) {
            foreach(Control c in root.Controls) {
                var card=c as OptionCard;
                if(card!=null && card.GetPreferredSize(new Size(card.Width,0)).Height > card.Height+2) throw new Exception("Option clipped: "+card.Text+" "+card.Height+"/"+card.GetPreferredSize(new Size(card.Width,0)).Height);
                var label=c as Label;
                if(label!=null && !(label is LinkLabel)) {
                    var measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(Math.Max(1,label.Width),10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                    if(measured.Height>label.Height+3) throw new Exception("Label clipped: "+label.Text);
                }
                CheckLayout(c);
            }
        }
        static void ProfileTests() {
            Test("Gaming profile applies and restores all values including absence", delegate {
                var h = new FakeHost(); var s = new MemoryStore(); using (var e = new Engine(h,s)) {
                    e.Enable(ProfileOptions.Gaming()); Assert(e.IsActive && h.Values["game-mode"][0] == 1 && h.Values["capture-history"][0] == 0 && h.Values["mouse-acceleration"][2] == 0, "Profile incomplete");
                    Assert(s.Load().SettingsBackup.Count == 5 && s.Load().SettingsBackup.All(x => x.Attempted), "Recovery record incomplete");
                    e.Disable(); Assert(!h.Values.ContainsKey("capture-history") && !h.Values.ContainsKey("capture-dvr") && h.Values["capture-app"][0] == 1 && h.Values["game-mode"][0] == 0 && h.Values["mouse-acceleration"].SequenceEqual(new [] { 6,10,1 }), "Original values not restored");
                }
            });
            foreach (string id in Settings.Selected(ProfileOptions.Gaming())) {
                string point = id;
                Test("Partial write rollback: " + point, delegate { var h = new FakeHost { Fail = "write-" + point }; var s = new MemoryStore(); using(var e = new Engine(h,s)) { Throws(delegate { e.Enable(ProfileOptions.Gaming()); }); Assert(h.Values["game-mode"][0] == 0 && h.Values["capture-app"][0] == 1 && h.Values["mouse-acceleration"][2] == 1 && !h.Values.ContainsKey("capture-history") && !h.Values.ContainsKey("capture-dvr") && h.Current == FakeHost.Original && s.Load().Phase == "Disabled", "Partial change remained"); } });
            }
            Test("Disabled options are not read or written", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(new ProfileOptions()); Assert(s.Load().SettingsBackup.Count == 0 && h.Values["game-mode"][0] == 0 && h.Values["mouse-acceleration"][2] == 1, "Unselected setting changed"); e.Disable(); } });
            Test("Settings changed externally are preserved and detected", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(ProfileOptions.Gaming()); h.Values["mouse-acceleration"] = new [] { 3, 7, 2 }; Assert(!e.IsActive, "Drift not detected"); e.Disable(); Assert(h.Values["mouse-acceleration"].SequenceEqual(new [] { 3,7,2 }), "External mouse preference overwritten"); } });
            Test("Setting restoration failure still restores power and supports retry", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(ProfileOptions.Gaming()); h.Fail = "write-capture-app"; Throws(delegate { e.Disable(); }); Assert(h.Current == FakeHost.Original && s.Load().Phase == "Restoring", "Recovery stopped too early"); } using(var e = new Engine(h,s)) { e.Disable(); Assert(e.State.Phase == "Disabled" && h.Values["capture-app"][0] == 1, "Recovery failed"); } });
            Test("Restore journal survives process restart", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(ProfileOptions.Gaming()); } using(var e = new Engine(h,s)) { e.Disable(); Assert(h.Values["game-mode"][0] == 0 && h.Values["mouse-acceleration"][2] == 1, "Restart lost originals"); } });
            Test("Silent setting rejection rolls back", delegate { var h = new FakeHost { IgnoreWrite = true }; var s = new MemoryStore(); using(var e = new Engine(h,s)) { Throws(delegate { e.Enable(ProfileOptions.Gaming()); }); Assert(h.Current == FakeHost.Original && s.Load().Phase == "Disabled", "Readback rejection not handled"); } });
            Test("Advanced power failure rolls back before applying preferences", delegate { var h = new FakeHost { Fail = "configure" }; var s = new MemoryStore(); using(var e = new Engine(h,s)) { Throws(delegate { e.Enable(new ProfileOptions { CpuReady = true, PcieReady = true, GameMode = true }); }); Assert(h.Current == FakeHost.Original && h.Plans.Count == 1 && h.Values["game-mode"][0] == 0, "Advanced failure leaked"); } });
            Test("Originals stay untouched on repeated Enable", delegate { var h = new FakeHost(); var s = new MemoryStore(); using(var e = new Engine(h,s)) { e.Enable(ProfileOptions.Gaming()); Throws(delegate { e.Enable(ProfileOptions.Gaming()); }); Assert(s.Load().SettingsBackup.First(x => x.Id == "game-mode").Original[0] == 0, "Original overwritten"); } });
            Test("Legacy XML loads and unknown setting IDs are rejected", delegate {
                string dir = Path.Combine(Path.GetTempPath(), "OpenAntiLag-test-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
                try { string path = Path.Combine(dir,"state.xml"); var store = new XmlStateStore(path); File.WriteAllText(path,"<ProfileState><Phase>Disabled</Phase><TimerRequested>false</TimerRequested></ProfileState>"); Assert(store.Load().SettingsBackup.Count == 0 && store.Load().SchemaVersion == 1, "Legacy state broken"); store.Save(new ProfileState { SettingsBackup = new List<SettingBackup> { new SettingBackup { Id = "arbitrary-registry-write" } } }); Throws(delegate { store.Load(); }); }
                finally { Directory.Delete(dir,true); }
            });
        }
    }
}
