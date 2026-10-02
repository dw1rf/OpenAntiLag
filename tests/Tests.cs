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
                ProfileTests(); count += MachineTests.Run();
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
                        Console.WriteLine("PASS UI: Enable, active state, Disable, restored state, narrow layout");
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

