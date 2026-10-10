using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenAntiLag {
    public class ProfileOptions {
        public bool GameMode;
        public bool DisableCapture;
        public bool DisableMouseAcceleration;
        public bool CpuReady;
        public bool PcieReady;
        public bool Timer;
        public static ProfileOptions Gaming() { return new ProfileOptions { GameMode = true, DisableCapture = true, DisableMouseAcceleration = true }; }
    }
    public class SettingBackup {
        public string Id;
        public int[] Original;
        public bool Attempted;
    }
    public static class Settings {
        public static string[] Selected(ProfileOptions o) {
            var ids = new List<string>();
            if (o.GameMode) ids.Add("game-mode");
            if (o.DisableCapture) { ids.Add("capture-history"); ids.Add("capture-app"); ids.Add("capture-dvr"); }
            if (o.DisableMouseAcceleration) ids.Add("mouse-acceleration");
            return ids.ToArray();
        }
        public static int[] Target(string id) {
            switch (id) {
                case "game-mode": return new [] { 1 };
                case "capture-history": case "capture-app": case "capture-dvr": return new [] { 0 };
                case "mouse-acceleration": return new [] { 0, 0, 0 };
                default: throw new InvalidDataException("Неизвестная настройка: " + id);
            }
        }
        public static bool Equal(int[] a, int[] b) { return a == null ? b == null : b != null && a.SequenceEqual(b); }
        public static void Validate(SettingBackup b) {
            if (b == null) throw new InvalidDataException("Повреждён журнал настроек.");
            int count = Target(b.Id).Length;
            if (b.Original != null && b.Original.Length != count) throw new InvalidDataException("Повреждён снимок настройки: " + b.Id);
            if (b.Id == "mouse-acceleration" && b.Original == null) throw new InvalidDataException("Нет исходных параметров мыши.");
        }
    }
}
