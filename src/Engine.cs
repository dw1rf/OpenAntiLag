using System;
using System.IO;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace OpenAntiLag {
    public class ProfileState {
        public string Phase = "Disabled";
        public string OriginalPlan;
        public string OwnedPlan;
        public bool TimerRequested;
        public int SchemaVersion = 1;
        public ProfileOptions Options = new ProfileOptions();
        public List<SettingBackup> SettingsBackup = new List<SettingBackup>();
    }
    public interface IStateStore {
        ProfileState Load();
        void Save(ProfileState state);
    }
    public interface IHost {
        string ActivePlan();
        bool HasPlan(string id);
        void CreatePlan(string id);
        void Activate(string id);
        void DeletePlan(string id);
        void StartTimer();
        void StopTimer();
        int[] ReadSetting(string id);
        void WriteSetting(string id, int[] value);
        void ConfigurePlan(string id, ProfileOptions options);
    }
    public sealed class XmlStateStore : IStateStore {
        readonly string path;
        public XmlStateStore(string path) { this.path = path; }
        public ProfileState Load() {
            if (!File.Exists(path)) return new ProfileState();
            using (var stream = File.OpenRead(path)) {
                var state = (ProfileState)new XmlSerializer(typeof(ProfileState)).Deserialize(stream);
                if (state.SchemaVersion < 1 || state.SchemaVersion > 2) throw new InvalidDataException("Неподдерживаемая версия состояния.");
                if (state.Options == null || state.SettingsBackup == null) throw new InvalidDataException("Повреждено состояние профиля.");
                foreach (var entry in state.SettingsBackup) Settings.Validate(entry);
                if (state.SettingsBackup.Select(x => x.Id).Distinct().Count() != state.SettingsBackup.Count) throw new InvalidDataException("Дублирующиеся настройки в журнале.");
                Guid a, b;
                if (state.Phase != "Disabled" && state.Phase != "Enabled" && state.Phase != "Applying" && state.Phase != "Restoring")
                    throw new InvalidDataException("Неизвестное состояние профиля. Сохраните файл состояния для диагностики.");
                if (state.Phase != "Disabled" && (!Guid.TryParse(state.OriginalPlan, out a) || !Guid.TryParse(state.OwnedPlan, out b) || a == b))
                    throw new InvalidDataException("Повреждена резервная копия плана. Автоматические изменения заблокированы.");
                return state;
            }
        }
        public void Save(ProfileState state) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                new XmlSerializer(typeof(ProfileState)).Serialize(stream, state);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
    public sealed class Engine : IDisposable {
        readonly IHost host;
        readonly IStateStore store;
        public ProfileState State { get; private set; }
        public bool TimerRunning { get; private set; }
        public Engine(IHost host, IStateStore store) { this.host = host; this.store = store; State = store.Load(); }
        public bool IsActive { get { return State.Phase == "Enabled" && host.ActivePlan() == State.OwnedPlan && State.SettingsBackup.All(x => Settings.Equal(host.ReadSetting(x.Id), Settings.Target(x.Id))); } }
        public void Resume() {
            if (State.Phase == "Enabled" && State.TimerRequested && IsActive) { host.StartTimer(); TimerRunning = true; }
        }
        public void Enable(bool timer) { Enable(new ProfileOptions { Timer = timer }); }
        public void Enable(ProfileOptions options) {
            if (State.Phase != "Disabled") throw new InvalidOperationException("Сначала восстановите предыдущий профиль.");
            var pending = new ProfileState { Phase = "Applying", SchemaVersion = 2, OriginalPlan = host.ActivePlan(), OwnedPlan = Guid.NewGuid().ToString(), TimerRequested = options.Timer, Options = options };
            foreach (string id in Settings.Selected(options)) pending.SettingsBackup.Add(new SettingBackup { Id = id, Original = host.ReadSetting(id) });
            // Persist the recovery record BEFORE the first system change.
            store.Save(pending);
            State = pending;
            try {
                host.CreatePlan(State.OwnedPlan);
                host.ConfigurePlan(State.OwnedPlan, options);
                host.Activate(State.OwnedPlan);
                if (host.ActivePlan() != State.OwnedPlan) throw new IOException("Windows не применила план питания.");
                foreach (var entry in State.SettingsBackup) {
                    entry.Attempted = true;
                    store.Save(State);
                    host.WriteSetting(entry.Id, Settings.Target(entry.Id));
                    if (!Settings.Equal(host.ReadSetting(entry.Id), Settings.Target(entry.Id))) throw new IOException("Windows не сохранила настройку: " + entry.Id);
                }
                if (options.Timer) { host.StartTimer(); TimerRunning = true; }
                State.Phase = "Enabled";
                store.Save(State);
            } catch (Exception original) {
                try { Disable(); }
                catch (Exception rollback) { throw new IOException("Не удалось включить профиль и завершить откат. Нажмите «Восстановить».\n" + original.Message + "\nОткат: " + rollback.Message, original); }
                throw new IOException("Включение отменено; изменения восстановлены.\n" + original.Message, original);
            }
        }
        public void Disable() {
            if (State.Phase == "Disabled") return;
            State.Phase = "Restoring";
            store.Save(State);
            host.StopTimer(); TimerRunning = false;
            // Persist per-setting progress so restoration can be retried after interruption.
            // Preserve values changed externally since Enable.
            var errors = new List<string>();
            foreach (var entry in State.SettingsBackup.AsEnumerable().Reverse()) {
                if (!entry.Attempted) continue;
                try {
                    if (Settings.Equal(host.ReadSetting(entry.Id), Settings.Target(entry.Id))) {
                        host.WriteSetting(entry.Id, entry.Original);
                        if (!Settings.Equal(host.ReadSetting(entry.Id), entry.Original)) throw new IOException("Не удалось вернуть исходное значение.");
                    }
                    entry.Attempted = false;
                    store.Save(State);
                } catch (Exception e) { entry.Attempted = true; errors.Add(entry.Id + ": " + e.Message); }
            }
            try {
            // Respect a plan the user selected outside this application.
            if (host.ActivePlan() == State.OwnedPlan) {
                if (!host.HasPlan(State.OriginalPlan)) throw new IOException("Исходный план удалён. Выберите другой план в Windows и повторите восстановление.");
                host.Activate(State.OriginalPlan);
                if (host.ActivePlan() != State.OriginalPlan) throw new IOException("Не удалось восстановить исходный план.");
            }
            if (host.HasPlan(State.OwnedPlan)) host.DeletePlan(State.OwnedPlan);
            } catch (Exception e) { errors.Add("План питания: " + e.Message); }
            if (errors.Count > 0) throw new IOException("Восстановление не завершено. Повторите «Восстановить».\n" + String.Join("\n", errors));
            var done = new ProfileState();
            store.Save(done);
            State = done;
        }
        public void Dispose() { host.StopTimer(); TimerRunning = false; }
    }
}
