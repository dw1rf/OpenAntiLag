using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace OpenAntiLag {
    static class Program {
        public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAntiLag");
        public static void Log(string text) {
            try { Directory.CreateDirectory(DataDirectory); File.AppendAllText(Path.Combine(DataDirectory, "events.log"), DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine); }
            catch { /* Logging failure must not prevent restoration. */ }
        }
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (var mutex = new Mutex(true, "Local\\OpenAntiLag-" + Environment.UserName, out first)) {
                if (!first) { MessageBox.Show("Open AntiLag уже работает. Откройте его через значок в трее.", "Open AntiLag"); return; }
                try {
                    using (var engine = new Engine(new WindowsHost(), new XmlStateStore(Path.Combine(DataDirectory, "state.xml"))))
                    using (var form = new MainForm(engine, Array.IndexOf(args, "--tray") >= 0, false)) Application.Run(form);
                } catch (Exception error) { Log(error.ToString()); MessageBox.Show("Приложение не запущено.\n" + error.Message + "\n\nЖурнал: " + DataDirectory, "Open AntiLag", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
