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
        public string CopiedFrom;
        public readonly Dictionary<string, int[]> Values = new Dictionary<string, int[]> { { "game-mode", new [] { 0 } }, { "capture-app", new [] { 1 } }, { "mouse-acceleration", new [] { 6, 10, 1 } } };
        public bool IgnoreWrite;
        public Action BeforeActivePlan;
        public string ActivePlan() { if(BeforeActivePlan!=null)BeforeActivePlan();return Current; }
        public bool HasPlan(string id) { return Plans.Contains(id); }
        void Check(string action) { if (Fail == action) { Fail = null; throw new IOException("Injected failure: " + action); } }
        public void CreatePlan(string id, string source) { if(!Plans.Contains(source))throw new IOException("Missing source plan");CopiedFrom=source;Writes++; Plans.Add(id); Check("create"); }
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
            if(args.Length==1&&args[0]=="--network-local-check") {
                try {var ct=System.Threading.CancellationToken.None;var transport=new SystemProbeTransport();var ping=transport.Ping(System.Net.IPAddress.Loopback,800,ct).GetAwaiter().GetResult();Console.WriteLine("Loopback ICMP: "+ping.Detail);var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();try{int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;var tcp=transport.Tcp(System.Net.IPAddress.Loopback,port,1000,ct).GetAwaiter().GetResult();Assert(tcp.Success,"Local TCP probe failed");Console.WriteLine("PASS loopback TCP connect");}finally{listener.Stop();}var performance=new PerformanceMonitor();performance.Run(0,5,1000,ct).GetAwaiter().GetResult();var readings=performance.Snapshot();Assert(readings.Any(s=>s.CpuPercent.HasValue)&&readings.Any(s=>s.RamTotal>0),"CPU/RAM native APIs failed");Console.WriteLine("PASS native CPU/RAM read-only samples="+readings.Length);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
            }
            if(args.Length==1&&args[0]=="--network-read-check") {
                try {var adapters=NetworkAdapters.Read();Console.WriteLine("Read-only adapters="+adapters.Length);Console.WriteLine("Process endpoints="+GameConnections.Read().Count);var host=new WindowsNetworkHost();foreach(var adapter in adapters.Where(a=>a.Type!="Loopback"))foreach(int family in new[]{4,6})try{var dns=host.ReadDns(adapter.Id,family);Console.WriteLine(adapter.Name+" IPv"+family+" DNS automatic="+dns.Automatic+" static="+String.Join(",",dns.Servers));}catch(Exception error){Console.WriteLine("DNS read limitation: "+error.Message);}Console.WriteLine(WindowsNetworkHost.Capability("DNS"));Console.WriteLine(WindowsNetworkHost.Capability("QoS"));return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
            }
            if(args.Length==2&&args[0]=="--shader-fixture")return ShaderTests.Fixture(args[1]);
            if(args.Length==3&&args[0]=="--shader-module-check")return ShaderTests.ModuleCheck(args[1],args[2]);
            if(args.Length==2&&args[0]=="--shader-scan") {try {var module=new ShaderEngine(args[1]);var result=module.Run("scan",null,Console.WriteLine,System.Threading.CancellationToken.None).GetAwaiter().GetResult();Console.WriteLine("Parsed games: "+ShaderProtocol.Games(result).Count);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
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
            if(args.Length>=1&&args[0]=="--input-fixture") {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                using(var e=new Engine(new FakeHost(),new MemoryStore()))using(var f=new MainForm(e,false,true,null,true)) {
                    f.Text="OpenAntiLag input fixture";
                    foreach(var option in FindOptions(f)) {var box=option;box.CheckedChanged+=delegate{Console.WriteLine("CHECK "+box.Text+"="+box.Checked);Console.Out.Flush();};}
                    if(args.Length>1&&args[1]=="small")f.ClientSize=new Size(630,560);
                    Application.Run(f);
                }return 0;
            }
            if(args.Length==1&&args[0]=="--player-ui-check")return PlayerUiCheck();
            if(args.Length == 1 && args[0] == "--ui-check") return UiCheck();
            if (args.Length == 1 && args[0] == "--inspect") {
                try { var host = new WindowsHost(); Console.WriteLine("Read-only Windows adapter check"); Console.WriteLine("Power plan: " + host.ActivePlan()); foreach (string id in Settings.Selected(ProfileOptions.Gaming())) { var value = host.ReadSetting(id); Console.WriteLine(id + ": " + (value == null ? "absent" : String.Join(",", value))); } return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if(args.Length>=2&&args[0]=="--player-preview") {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int result=0;
                using(var e=new Engine(new FakeHost(),new MemoryStore()))using(var f=new MainForm(e,false,true)) {
                    var context=f.Workspace.Context;context.Preferences.Games.Add(new InstalledGame {Id="steam:730",Name="Counter-Strike 2 · предпросмотр"});context.Preferences.SelectedGame="steam:730";
                    var tick=new System.Windows.Forms.Timer {Interval=700};tick.Tick+=delegate{tick.Stop();try{if(args.Length>2)f.Workspace.Navigation.ShowPage(args[2]);f.PerformLayout();using(var bitmap=new Bitmap(f.ClientSize.Width,f.ClientSize.Height)){f.Workspace.DrawToBitmap(bitmap,new Rectangle(Point.Empty,f.ClientSize));bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);}}catch(Exception error){Console.Error.WriteLine(error);result=1;}finally{f.Close();}};f.Shown+=delegate{tick.Start();};Application.Run(f);tick.Dispose();
                }return result;
            }
            if (args.Length >= 2 && args[0] == "--preview") {
                int previewExit=0;
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var e = new Engine(new FakeHost(), new MemoryStore())) {
                if(args.Length > 2 && args[2] == "active") e.Enable(new ProfileOptions { GameMode=true, DisableCapture=true, DisableMouseAcceleration=true, CpuReady=true, PcieReady=true, Timer=true });
                using (var f = new MainForm(e, false, true,null,true)) {
                    if(args.Length > 2 && (args[2] == "small"||args[2]=="shaders-small")) f.ClientSize = new Size(630,560);
                    if(args.Length>2&&args[2]=="advanced")f.ClientSize=new Size(800,1400);
                    if(args.Length > 3) {
                        float scale=float.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture);
                        var fonts=new Dictionary<Control,Font>(); CaptureFonts(f,fonts);
                        f.SuspendLayout(); f.Scale(new SizeF(scale,scale));
                        foreach(var pair in fonts) pair.Key.Font=new Font(pair.Value.FontFamily,pair.Value.Size*scale,pair.Value.Style);
                        f.ResumeLayout(true);
                    }
                    if(args.Length>2 && (args[2]=="nvidia" || args[2]=="amd")) ((TabControl)f.Controls.Find("mainTabs",true)[0]).SelectedIndex=args[2]=="amd"?2:1;
                    if(args.Length>2 && args[2].StartsWith("shaders")) ((TabControl)f.Controls.Find("mainTabs",true)[0]).SelectedIndex=3;
                    var newPages=new[]{"network","traffic","dns","statistics"};if(args.Length>2&&Array.IndexOf(newPages,args[2])>=0)((TabControl)f.Controls.Find("mainTabs",true)[0]).SelectedIndex=4+Array.IndexOf(newPages,args[2]);
                    var tick = new System.Windows.Forms.Timer { Interval = 600 };
                    tick.Tick += delegate {
                        tick.Stop();
                        try {
                        if(args.Length>2&&args[2]=="advanced")((Panel)f.Controls.Find("profileOptionsScroll",true)[0]).AutoScrollPosition=new Point(0,10000);
                        if(args.Length>2&&args[2].StartsWith("shaders")){var list=(ListView)f.Controls.Find("shaderGames",true)[0];list.Items[0].Selected=true;list.Refresh();}
                        Console.WriteLine(WindowTheme.Inspect(f.Handle));
                        using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height)) { f.Controls[0].DrawToBitmap(bmp, new Rectangle(Point.Empty, f.ClientSize)); bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                        CheckLayout(f);
                        }catch(Exception error){Console.Error.WriteLine(error);previewExit=1;}finally{f.Close();}
                    };
                    f.Shown += delegate { tick.Start(); }; Application.Run(f); tick.Dispose();
                } }
                return previewExit;
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
                Test("Recover after process interruption", delegate { var h = new FakeHost(); var s = new MemoryStore(); string owned = Guid.NewGuid().ToString(); s.Save(new ProfileState { Phase = "Applying", OriginalPlan = FakeHost.Original, OwnedPlan = owned }); h.CreatePlan(owned,FakeHost.Original); h.Activate(owned); using(var e = new Engine(h,s)) { e.Disable(); Assert(h.Current == FakeHost.Original && h.Plans.Count == 1, "Recovery failed"); } });
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
                ProfileTests(); count += MachineTests.Run(); count += UpdateTests.Run(); count += NvidiaTests.Run();count += AmdTests.Run();count += ShaderTests.Run();count += NetworkTests.Run();count += PlayerTests.Run();                Test("UAC launches on STA with exact path and working directory",delegate {ElevatedProcess.Run(System.Windows.Forms.Application.ExecutablePath,"--machine check test",delegate(System.Diagnostics.ProcessStartInfo info){Assert(System.Threading.Thread.CurrentThread.GetApartmentState()==System.Threading.ApartmentState.STA,"Not STA");Assert(info.Verb=="runas"&&info.UseShellExecute&&info.WorkingDirectory==Path.GetDirectoryName(info.FileName)&&info.Arguments=="--machine check test","Wrong UAC request");return 0;});});
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
            using(var form=new MainForm(engine,false,true,null,true)) {
                var tick=new System.Windows.Forms.Timer { Interval=800 };
                tick.Tick+=async delegate {
                    tick.Stop();
                    try {
                        var on=(Button)form.Controls.Find("enable",true)[0]; var off=(Button)form.Controls.Find("disable",true)[0];
                        await WaitUntil(delegate { return on.Enabled; });
                        Assert(form.Controls.Find("advanced_hags",true).Length==1&&!((OptionCard)form.Controls.Find("advanced_hags",true)[0]).Checked,"HAGS opt-in missing or checked by default");
                        CheckLayout(form); on.PerformClick();
                        await WaitUntil(delegate { return engine.State.Phase=="Enabled" && off.Enabled; });
                        Assert(!FindLabels(form).Any(x=>x.Text=="Нужно восстановление"||x.Text=="Нужна перезагрузка ПК"),"Safe profile incorrectly needs system recovery");
                        Assert(on.Enabled&&((OptionCard)form.Controls.Find("advanced_hags",true)[0]).Enabled,"Active profile locked selection"); CheckLayout(form); off.PerformClick();
                        await WaitUntil(delegate { return engine.State.Phase=="Disabled" && on.Enabled; });
                        var hagsOption=(OptionCard)form.Controls.Find("advanced_hags",true)[0];hagsOption.Checked=true;on.PerformClick();await WaitUntil(()=>off.Enabled);
                        Assert(off.Text=="Отключить"&&!FindLabels(form).Any(x=>x.Text=="Восстановите старый профиль"),"New selected profile classified as legacy");off.PerformClick();await WaitUntil(()=>on.Enabled);Assert(!hagsOption.Checked,"HAGS checkbox not reset after restore");
                        form.ClientSize=new Size(630,560); form.PerformLayout(); CheckLayout(form);
                        Assert(on.Bottom<=on.Parent.ClientSize.Height && off.Enabled,"Action state/layout invalid");
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
                        ((Button)amdPage.Controls.Find("readAmd",true)[0]).PerformClick();await WaitUntil(()=>amdApply.Enabled);amdApply.PerformClick();await WaitUntil(()=>amdApply.Enabled);Assert(amdStore.Load()!=null&&amdStore.Load().Phase=="Applied","AMD UI apply failed");
                        amdRestore.PerformClick();await WaitUntil(()=>amdRestore.Enabled);Assert(amdStore.Load()==null&&amdDriver.Values["chill"]==1,"AMD UI restore failed");tabs.TabPages.Remove(amdPage);amdPage.Dispose();
                        using(var legacyEngine=new Engine(new FakeHost(),new MemoryStore())) {
                            legacyEngine.Enable(new ProfileOptions {CpuReady=true,PcieReady=true,Timer=true});
                            legacyEngine.State.SchemaVersion=2;
                            var legacySystem=new TestSystemClient {Pending=true};legacySystem.Value.Phase="Enabled";
                            using(var legacyForm=new MainForm(legacyEngine,false,true,legacySystem,true)) {
                                legacyForm.Show();
                                var legacyOn=(Button)legacyForm.Controls.Find("enable",true)[0];var legacyOff=(Button)legacyForm.Controls.Find("disable",true)[0];
                                await WaitUntil(()=>legacyOff.Enabled);
                                Assert(legacyOn.Enabled&&legacyOff.Text=="Восстановить"&&FindLabels(legacyForm).Any(x=>x.Text=="Восстановите старый профиль"),"Migration prompt missing");
                                legacyOff.PerformClick();await WaitUntil(()=>legacyEngine.State.Phase=="Disabled"&&legacyOn.Enabled);
                                Assert(legacyOn.Enabled&&((OptionCard)legacyForm.Controls.Find("advanced_hags",true)[0]).Enabled&&legacySystem.DisableCalls==1,"Pending reboot locked controls");
                                legacySystem.Pending=false;await legacyForm.RefreshState();
                                Assert(legacyOn.Enabled,"Cannot enable after restart");legacyOn.PerformClick();await WaitUntil(()=>legacyEngine.State.Phase=="Enabled"&&legacyOff.Enabled);
                                Assert(!legacyEngine.State.Options.CpuReady&&!legacyEngine.State.Options.PcieReady&&!legacyEngine.State.TimerRequested,"Legacy aggressive options carried into safe profile");
                                legacyForm.Close();
                            }
                        }
                        Console.WriteLine("PASS UI: system actions, GPU tabs, NVIDIA apply/restore buttons on simulated driver, narrow layout");
                        Console.WriteLine("PASS UI: legacy recovery, reboot gate and reset of aggressive options");
                        bool shaderLocked=false,waitForStop=false;int compileCalls=0;
                        Func<string,string,Action<string>,System.Threading.CancellationToken,Task<string>> shaderRun=async (command,id,log,ct)=>{
                            if(command=="scan")return ShaderTests.Listing;
                            if(command=="queue"){compileCalls++;if(waitForStop)await Task.Delay(System.Threading.Timeout.Infinite,ct);}
                            return "done";
                        };
                        using(var shaderPage=new ShaderPage(false,()=>{if(shaderLocked)return false;shaderLocked=true;return true;},()=>{shaderLocked=false;},null,shaderRun)) {
                            tabs.TabPages.Add(shaderPage);tabs.SelectedTab=shaderPage;
                            var shaderScan=(Button)shaderPage.Controls.Find("scanShaders",true)[0];var shaderCompile=(Button)shaderPage.Controls.Find("compileShaders",true)[0];var shaderStop=(Button)shaderPage.Controls.Find("stopShaders",true)[0];var shaderGames=(ListView)shaderPage.Controls.Find("shaderGames",true)[0];
                            shaderScan.PerformClick();await WaitUntil(()=>shaderGames.Items.Count==4&&!shaderLocked);
                            shaderGames.Items[1].Selected=true;Assert(!shaderCompile.Enabled,"Recording-only game can compile");shaderGames.Items[1].Selected=false;shaderGames.Items[0].Selected=true;
                            Assert(shaderCompile.Enabled&&shaderCompile.Text.Contains("(1)"),"Selection count not shown");
                            shaderCompile.Focus();shaderGames.Refresh();
                            using(var bitmap=new Bitmap(shaderGames.Width,shaderGames.Height)){shaderGames.DrawToBitmap(bitmap,shaderGames.ClientRectangle);var row=shaderGames.Items[0].Bounds;Assert(bitmap.GetPixel(row.Left+2,row.Top+row.Height/2).ToArgb()==Theme.Accent.ToArgb(),"Selected row not visible without focus");}
                            Assert(shaderCompile.Enabled,"Ready game cannot compile");shaderCompile.PerformClick();await WaitUntil(()=>compileCalls==1&&!shaderLocked);
                            shaderGames.Items[0].Selected=true;waitForStop=true;shaderCompile.PerformClick();await WaitUntil(()=>shaderStop.Enabled&&shaderLocked);
                            Assert(!shaderScan.Enabled,"Scan enabled during compile");shaderStop.PerformClick();await WaitUntil(()=>!shaderLocked&&shaderCompile.Enabled);
                            Assert(((Label)shaderPage.Controls.Find("shaderStatus",true)[0]).Text.StartsWith("Остановлено"),"Cancellation not shown");
                            CheckLayout(shaderPage);tabs.TabPages.Remove(shaderPage);
                        }
                        Console.WriteLine("PASS UI: shader scan, unsupported game guard, compile, shared operation lock and cancellation");
                        var slowHost=new FakeHost();using(var slowEngine=new Engine(slowHost,new MemoryStore()))using(var slowForm=new MainForm(slowEngine,false,true,null,true)) {
                            slowEngine.Enable(ProfileOptions.Gaming());slowForm.Show();await Task.Delay(100);await slowForm.RefreshState();
                            using(var entered=new System.Threading.ManualResetEventSlim())using(var finish=new System.Threading.ManualResetEventSlim()) {
                                slowHost.BeforeActivePlan=()=>{entered.Set();finish.Wait(5000);};var refresh=slowForm.RefreshState();
                                try {await WaitUntil(()=>entered.IsSet);Assert(slowForm.TryAcquireGpu(),"Background profile read blocks GPU operation");Assert(!slowForm.TryAcquireGpu(),"Concurrent GPU operation allowed");slowForm.ReleaseGpu();}
                                finally {finish.Set();slowHost.BeforeActivePlan=null;}
                                await refresh;
                            }
                            slowForm.Close();
                        }
                        Console.WriteLine("PASS UI: background profile reading does not block GPU, real GPU exclusion remains");
                        var restoreHost=new FakeHost();var restoreSystem=new TestSystemClient();
                        using(var restoreEngine=new Engine(restoreHost,new MemoryStore()))using(var restoreForm=new MainForm(restoreEngine,false,true,restoreSystem,true)) {
                            restoreForm.Show();var basic=(Button)restoreForm.Controls.Find("restoreBasic",true)[0];var resultLabel=(TextBox)restoreForm.Controls.Find("recoveryResult",true)[0];
                            await WaitUntil(()=>((Button)restoreForm.Controls.Find("enable",true)[0]).Enabled);
                            int originalWrites=restoreHost.Writes;basic.PerformClick();await WaitUntil(()=>resultLabel.Text.StartsWith("Сохранённого"));Assert(restoreHost.Writes==originalWrites,"Empty backup changed Windows");
                            restoreEngine.Enable(ProfileOptions.Gaming());restoreSystem.FailState=true;await restoreForm.RefreshState();
                            Assert(basic.Enabled&&((Button)restoreForm.Controls.Find("enable",true)[0]).Enabled&&((OptionCard)restoreForm.Controls.Find("advanced_hags",true)[0]).Enabled,"System state failure locked controls");
                            restoreHost.Fail="write-game-mode";basic.PerformClick();await WaitUntil(()=>basic.Enabled&&resultLabel.Text.StartsWith("Восстановление основных не завершено"));
                            Assert(restoreEngine.State.Phase=="Restoring","Failed restore lost recovery state");basic.PerformClick();await WaitUntil(()=>basic.Enabled&&restoreEngine.State.Phase=="Disabled");
                            Assert(restoreSystem.DisableCalls==0&&restoreHost.Current==FakeHost.Original&&restoreHost.Values["game-mode"][0]==0,"Basic recovery touched system helper or failed restoration");
                            restoreSystem.FailState=false;await restoreForm.RefreshState();Assert(((Button)restoreForm.Controls.Find("enable",true)[0]).Enabled,"Profile remains blocked after basic recovery");
                            restoreForm.ClientSize=new Size(630,560);CheckLayout(restoreForm);Assert(basic.Bottom<=basic.Parent.ClientSize.Height,"Recovery button clipped");restoreForm.Close();
                        }
                        Console.WriteLine("PASS UI: basic recovery without machine journal, no-backup no-op, failure retry and profile re-enable");
                        foreach(float scale in new[]{1f,1.25f,1.5f,2f})using(var scaledEngine=new Engine(new FakeHost(),new MemoryStore()))using(var scaledForm=new MainForm(scaledEngine,false,true,null,true)) {
                            scaledForm.Show();scaledForm.ClientSize=new Size(800,930);var fonts=new Dictionary<Control,Font>();CaptureFonts(scaledForm,fonts);scaledForm.Scale(new SizeF(scale,scale));foreach(var pair in fonts)pair.Key.Font=new Font(pair.Value.FontFamily,pair.Value.Size*scale,pair.Value.Style);scaledForm.PerformLayout();
                            var scaledTabs=(TabControl)scaledForm.Controls.Find("mainTabs",true)[0];Assert(scaledTabs.TabCount==8,"New modules missing");
                            for(int index=4;index<8;index++){scaledTabs.SelectedIndex=index;scaledForm.PerformLayout();CheckLayout(scaledTabs.TabPages[index]);Assert(scaledTabs.TabPages[index].Enabled,"Diagnostic page disabled");}
                            scaledForm.Close();
                        }
                        Console.WriteLine("PASS UI: all network modules, wrapping tabs and 100/125/150/200 percent scaling");
                        var probe=new FakeProbeTransport();var uiMonitor=new NetworkMonitor(probe,()=>new AdapterSnapshot[0]);using(var page=new NetworkPage(false,uiMonitor)) {
                            tabs.TabPages.Add(page);tabs.SelectedTab=page;var start=(Button)page.Controls.Find("Начать измерение",true)[0];start.PerformClick();await Task.Delay(50);Assert(probe.Calls==0,"Network started without consent");Assert(((Label)page.Controls.Find("toolStatus",true)[0]).Text.Contains("Разрешите"),"Missing consent error");tabs.TabPages.Remove(page);
                        }
                        Console.WriteLine("PASS UI: network refuses unapproved probes and remains responsive");
                    } catch(Exception ex) { Console.Error.WriteLine(ex); result=1; }
                    form.Close();
                };
                form.Shown+=delegate { tick.Start(); }; Application.Run(form); tick.Dispose();
            }
            return result;
        }
        static int PlayerUiCheck() {
            Control.CheckForIllegalCrossThreadCalls=true;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);int result=0;
            using(var engine=new Engine(new FakeHost(),new MemoryStore()))using(var form=new MainForm(engine,false,true)) {
                var context=form.Workspace.Context;context.Preferences.Games.Add(new InstalledGame {Id="steam:730",Name="Counter-Strike 2 · UI fixture"});context.Preferences.SelectedGame="steam:730";
                var timer=new System.Windows.Forms.Timer {Interval=700};timer.Tick+=async delegate {timer.Stop();try {
                    await WaitUntil(()=>context.Initialized);context.Notice="Background UI dispatch fixture";await Task.Run(()=>context.Emit());var primary=(Button)form.Controls.Find("playerPrimary",true)[0];Assert(primary.Enabled,"Dashboard action disabled");primary.PerformClick();await WaitUntil(()=>engine.State.Phase=="Enabled"&&!context.Optimizer.Status.Busy);Assert(primary.Text=="Отключить оптимизацию","Primary did not reflect applied backend state");primary.PerformClick();await WaitUntil(()=>engine.State.Phase=="Disabled"&&!context.Optimizer.Status.Busy);
                    foreach(string page in new[]{"Home","Games","Optimization","Results","Settings","Recovery","Internet"}){form.Workspace.Navigation.ShowPage(page);form.PerformLayout();CheckLayout(form.Workspace);}
                    foreach(float scale in new[]{1f,1.25f,1.5f,2f})using(var scaledEngine=new Engine(new FakeHost(),new MemoryStore()))using(var scaled=new MainForm(scaledEngine,false,true)) {
                        scaled.Show();await WaitUntil(()=>scaled.Workspace.Context.Initialized);scaled.ClientSize=new Size(1020,700);var fonts=new Dictionary<Control,Font>();CaptureFonts(scaled,fonts);scaled.Scale(new SizeF(scale,scale));foreach(var pair in fonts)pair.Key.Font=new Font(pair.Value.FontFamily,pair.Value.Size*scale,pair.Value.Style);scaled.PerformLayout();
                        foreach(string page in new[]{"Home","Games","Optimization","Results","Settings","Recovery","Internet"}){scaled.Workspace.Navigation.ShowPage(page);scaled.PerformLayout();CheckLayout(scaled.Workspace);}
                        scaled.Close();
                    }
                    foreach(int index in new[]{0,1,2,3,4,5,6,7}){form.Workspace.ShowExpert(index,"Technical fixture");form.PerformLayout();var tools=(TabControl)form.Controls.Find("mainTabs",true)[0];Assert(tools.SelectedIndex==index,"Professional module inaccessible");Assert(tools.Top<0,"Technical tab bar was not hidden");CheckLayout(tools.TabPages[index]);}
                    using(var wizard=new SetupWizard(context)){wizard.Show(form);var skip=(Button)wizard.Controls.Find("Пропустить",true)[0];skip.PerformClick();Assert(context.Preferences.SetupComplete,"Wizard skip did not persist completion");}
                    Console.WriteLine("PASS player UI: real fake-host apply/disable, seven routes, eight preserved modules, wizard skip, 100/125/150/200 scaling");
                }catch(Exception error){Console.Error.WriteLine(error);result=1;}finally{form.Close();}};form.Shown+=delegate{timer.Start();};Application.Run(form);timer.Dispose();
            }return result;
        }
        static void CheckLayout(Control root) {
            foreach(Control c in root.Controls) {
                if(!c.Visible)continue;
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
        static IEnumerable<OptionCard> FindOptions(Control root) {foreach(Control child in root.Controls){var option=child as OptionCard;if(option!=null)yield return option;foreach(var nested in FindOptions(child))yield return nested;}}
        static IEnumerable<Label> FindLabels(Control root) {
            foreach(Control child in root.Controls) {var label=child as Label;if(label!=null)yield return label;foreach(var nested in FindLabels(child))yield return nested;}
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
