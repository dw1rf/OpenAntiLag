using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.ComponentModel;

namespace OpenAntiLag {
    public sealed class WindowsHost : IHost {
        bool timer;
        const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint period);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool MouseParameters(uint action, uint param, [In, Out] int[] values, uint flags);
        static string Id(string value) { return Guid.Parse(value).ToString(); }
        public static string Run(string file, string arguments) {
            var info = new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(info)) {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(20000)) { try { process.Kill(); process.WaitForExit(3000); } catch { } throw new IOException("Команда Windows не ответила за 20 секунд."); }
                string output = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new IOException("Windows отклонила команду (" + process.ExitCode + ").\n" + output + error);
                return output;
            }
        }
        string Power(string args) { return Run(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), args); }
        public string ActivePlan() {
            var match = Regex.Match(Power("/getactivescheme"), @"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            if (!match.Success) throw new IOException("Windows не сообщила идентификатор активного плана.");
            return Id(match.Value);
        }
        public bool HasPlan(string id) { return Power("/list").IndexOf(Id(id), StringComparison.OrdinalIgnoreCase) >= 0; }
        public void CreatePlan(string id) {
            Power("/duplicatescheme " + HighPerformance + " " + Id(id));
            Power("/changename " + Id(id) + " \"Open AntiLag Performance\"");
        }
        public void Activate(string id) { Power("/setactive " + Id(id)); }
        public void ConfigurePlan(string id, ProfileOptions options) {
            if (options.CpuReady) {
                Power("/setacvalueindex " + Id(id) + " SUB_PROCESSOR PROCTHROTTLEMIN 100");
                Power("/setacvalueindex " + Id(id) + " SUB_PROCESSOR PROCTHROTTLEMAX 100");
                Power("/setacvalueindex " + Id(id) + " SUB_PROCESSOR CPMINCORES 100");
            }
            if (options.PcieReady) Power("/setacvalueindex " + Id(id) + " SUB_PCIEXPRESS ASPM 0");
        }
        static string[] RegistryLocation(string id) {
            switch (id) {
                case "game-mode": return new [] { @"Software\Microsoft\GameBar", "AutoGameModeEnabled" };
                case "capture-history": return new [] { @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled" };
                case "capture-app": return new [] { @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled" };
                case "capture-dvr": return new [] { @"System\GameConfigStore", "GameDVR_Enabled" };
                default: throw new InvalidDataException("Неизвестный параметр реестра.");
            }
        }
        public int[] ReadSetting(string id) {
            Settings.Target(id);
            if (id == "mouse-acceleration") {
                int[] values = new int[3];
                if (!MouseParameters(0x0003, 0, values, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось прочитать параметры мыши.");
                return values;
            }
            var location = RegistryLocation(id);
            using (var key = Registry.CurrentUser.OpenSubKey(location[0])) {
                if (key == null || key.GetValue(location[1]) == null) return null;
                if (key.GetValueKind(location[1]) != RegistryValueKind.DWord) throw new InvalidDataException("Неожиданный тип параметра " + location[1] + ". Он не изменён.");
                return new [] { (int)key.GetValue(location[1]) };
            }
        }
        public void WriteSetting(string id, int[] value) {
            int count = Settings.Target(id).Length;
            if (value != null && value.Length != count) throw new InvalidDataException("Неверное значение настройки.");
            if (id == "mouse-acceleration") {
                if (value == null) throw new InvalidDataException("Нет параметров мыши для восстановления.");
                if (!MouseParameters(0x0004, 0, value, 3)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось изменить параметры мыши.");
                return;
            }
            var location = RegistryLocation(id);
            if (value == null) { using (var key = Registry.CurrentUser.OpenSubKey(location[0], true)) if (key != null) key.DeleteValue(location[1], false); }
            else using (var key = Registry.CurrentUser.CreateSubKey(location[0])) key.SetValue(location[1], value[0], RegistryValueKind.DWord);
        }
        public void DeletePlan(string id) { Power("/delete " + Id(id)); }
        public void StartTimer() {
            if (timer) return;
            if (timeBeginPeriod(1) != 0) throw new IOException("Windows отклонила запрос таймера 1 мс.");
            timer = true;
        }
        public void StopTimer() { if (timer) { timeEndPeriod(1); timer = false; } }
    }
    public static class Startup {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "OpenAntiLag";
        static string Command { get { return "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --tray"; } }
        public static bool Enabled {
            get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && String.Equals(key.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase); }
            set { using (var key = Registry.CurrentUser.CreateSubKey(Key)) { if (value) key.SetValue(Name, Command, RegistryValueKind.String); else key.DeleteValue(Name, false); } }
        }
    }
}
