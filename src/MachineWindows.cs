using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace OpenAntiLag {
    public sealed class MachineStore : IMachineStore {
        const string Path=@"SOFTWARE\OpenAntiLag\SystemProfile";
        public MachineState Load() {
            using(var key=Registry.LocalMachine.OpenSubKey(Path)) {
                string xml=key==null?null:key.GetValue("Journal") as string;
                if(xml==null) { if(key!=null && key.GetValue("Journal")!=null)throw new InvalidDataException("Неверный тип системного журнала."); return new MachineState(); }
                if(xml.Length>100000)throw new InvalidDataException("Системный журнал слишком большой.");
                using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) {
                    var state=(MachineState)new XmlSerializer(typeof(MachineState)).Deserialize(reader); MachineSettings.Validate(state); return state;
                }
            }
        }
        public void Save(MachineState state) {
            MachineSettings.Validate(state);
            var security=new RegistrySecurity();
            security.SetAccessRuleProtection(true,false);
            foreach(var sid in new [] {WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid}) security.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(sid,null),RegistryRights.FullControl,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
            security.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),RegistryRights.ReadKey,InheritanceFlags.ContainerInherit,PropagationFlags.None,AccessControlType.Allow));
            using(var key=Registry.LocalMachine.CreateSubKey(Path,RegistryKeyPermissionCheck.ReadWriteSubTree,security))
            using(var writer=new StringWriter()) { new XmlSerializer(typeof(MachineState)).Serialize(writer,state); key.SetValue("Journal",writer.ToString(),RegistryValueKind.String); key.Flush(); }
        }
    }
    public sealed class MachineHost : IMachineHost {
        static readonly Regex GuidPattern=new Regex(@"\{[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}");
        public string BootEntry() {
            string output=WindowsHost.Run(System.IO.Path.Combine(Environment.SystemDirectory,"bcdedit.exe"),"/enum {current} /v");
            var match=GuidPattern.Match(output);if(!match.Success)throw new IOException("Не удалось определить текущую запись загрузки Windows.");
            string id=Guid.Parse(match.Value).ToString("B");
            using(var obj=OpenObject(id)) if(Convert.ToUInt32(obj["Type"])!=0x10200003)throw new IOException("Текущая запись не является загрузчиком Windows.");
            return id;
        }
        public string BootSession() {
            using(var query=new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
            using(var results=query.Get()) { foreach(ManagementObject item in results)using(item)return (string)item["LastBootUpTime"]; }
            throw new IOException("Не удалось определить время загрузки Windows.");
        }
        public void Preflight(bool bcdChanges) {
            if(!MachineWorker.IsAdmin)throw new UnauthorizedAccessException("Нужны права администратора.");
            if(!bcdChanges)return;
            string drive=System.IO.Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
            try {
                var scope=new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption",new ConnectionOptions {EnablePrivileges=true});
                using(var query=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='"+drive+"'")))
                using(var volumes=query.Get()) {
                    bool found=false;
                    foreach(ManagementObject volume in volumes)using(volume) {found=true; if(Convert.ToUInt32(volume["ProtectionStatus"])!=0)throw new InvalidOperationException("Защита BitLocker активна или её состояние неизвестно. Изменения BCD заблокированы; защита автоматически не отключается.");}
                    if(!found)throw new InvalidOperationException("Не удалось проверить защиту системного тома. BCD не изменён.");
                }
            } catch(ManagementException error) {throw new IOException("Не удалось проверить BitLocker. Системные изменения не начаты.",error);}
        }
        static ManagementObject OpenObject(string id) {
            string guid=Guid.Parse(id).ToString("B");
            var scope=new ManagementScope(@"\\.\root\WMI",new ConnectionOptions {EnablePrivileges=true,Impersonation=ImpersonationLevel.Impersonate});scope.Connect();
            var obj=new ManagementObject(scope,new ManagementPath("BcdObject.Id=\""+guid+"\",StoreFilePath=\"\""),null);obj.Get();return obj;
        }
        static uint Element(string id) {
            switch(id) {case "useplatformclock":return 0x260000A2;case "useplatformtick":return 0x260000A4;case "disabledynamictick":return 0x260000A5;default:throw new InvalidDataException("Недопустимый элемент BCD.");}
        }
        public int? Read(string id,string bootEntry) {
            MachineSettings.Target(id);
            if(id=="hags" || id=="priority") {
                string path=id=="hags"?@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers":@"SYSTEM\CurrentControlSet\Control\PriorityControl";
                string name=id=="hags"?"HwSchMode":"Win32PrioritySeparation";
                using(var key=Registry.LocalMachine.OpenSubKey(path)) {if(key==null||key.GetValue(name)==null)return null;if(key.GetValueKind(name)!=RegistryValueKind.DWord)throw new IOException("Неверный тип "+name);return (int)key.GetValue(name);}
            }
            using(var obj=OpenObject(bootEntry))
            using(var result=obj.InvokeMethod("EnumerateElements",(ManagementBaseObject)null,null)) {
                if(result==null||!Convert.ToBoolean(result["ReturnValue"]))throw new IOException("Не удалось прочитать BCD.");
                var elements=result["Elements"] as ManagementBaseObject[];
                if(elements==null||elements.Length==0)throw new IOException("BCD не вернул элементы загрузчика.");
                int? value=null;
                foreach(var entry in elements)using(entry) {if(Convert.ToUInt32(entry["Type"])==Element(id))value=Convert.ToBoolean(entry["Boolean"])?1:0;}
                return value;
            }
        }
        public void Write(string id,string bootEntry,int? value) {
            MachineSettings.Target(id);
            if(id=="hags" || id=="priority") {
                string path=id=="hags"?@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers":@"SYSTEM\CurrentControlSet\Control\PriorityControl";
                string name=id=="hags"?"HwSchMode":"Win32PrioritySeparation";
                using(var key=Registry.LocalMachine.OpenSubKey(path,true)) {if(key==null)throw new IOException("Нет системного раздела "+name);if(value.HasValue)key.SetValue(name,value.Value,RegistryValueKind.DWord);else key.DeleteValue(name,false);key.Flush();} return;
            }
            if(value.HasValue&&value!=0&&value!=1)throw new InvalidDataException("Значение BCD должно быть логическим.");
            if(!value.HasValue && !Read(id,bootEntry).HasValue)return;
            string method=value.HasValue?"SetBooleanElement":"DeleteElement";
            using(var obj=OpenObject(bootEntry))
            using(var input=obj.GetMethodParameters(method)) {
                input["Type"]=Element(id);if(value.HasValue)input["Boolean"]=value.Value==1;
                using(var result=obj.InvokeMethod(method,input,null)) if(result==null||!Convert.ToBoolean(result["ReturnValue"]))throw new IOException("Windows отклонила изменение BCD: "+id);
            }
        }
    }
    public static class MachineWorker {
        public static bool IsAdmin {get{return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}}
        public static string Sid {get{return WindowsIdentity.GetCurrent().User.Value;}}
        public static int Run(string action,string expectedSid) {
            try {
                if(!IsAdmin || expectedSid!=Sid)throw new UnauthorizedAccessException("Подтвердите UAC для той же учётной записи Windows.");
                if(action=="check")return 0; // Read-only UAC probe, no system engine or settings accessed.
                bool created;
                using(var mutex=new Mutex(true,@"Global\OpenAntiLag-SystemProfile",out created)) {
                    bool owned=created;
                    try {
                        if(!owned) {try {owned=mutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}}
                        if(!owned)throw new IOException("Уже выполняется другая системная операция.");
                        var engine=new MachineEngine(new MachineHost(),new MachineStore(),Sid);
                        if(action=="enable")engine.Enable();else if(action=="disable")engine.Disable();else throw new InvalidOperationException("Неизвестная операция.");
                    } finally {if(owned)mutex.ReleaseMutex();}
                }
                return 0;
            } catch(Exception error) {System.Windows.Forms.MessageBox.Show(error.Message,"Open AntiLag — системный профиль",System.Windows.Forms.MessageBoxButtons.OK,System.Windows.Forms.MessageBoxIcon.Error);return 1;}
        }
        public static void Elevate(bool enable) {
            string action=enable?"enable":"disable";
            if(IsAdmin) {
                if(Run(action,Sid)!=0)throw new IOException("Системная операция не завершена. Подробности показаны в окне администратора.");
                return;
            }
            ElevatedProcess.Run(System.Windows.Forms.Application.ExecutablePath,"--machine "+action+" "+Sid);
        }
    }
}
