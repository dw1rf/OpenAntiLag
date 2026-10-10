using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace OpenAntiLag {
    public static class ElevatedProcess {
        public static void Run(string executable,string arguments,Func<ProcessStartInfo,int> launch=null) {
            if(!File.Exists(executable))throw new FileNotFoundException("Не найден EXE для запроса прав администратора.",executable);
            Exception failure=null;
            var thread=new Thread(delegate() {
                try {
                    var info=new ProcessStartInfo(executable,arguments) { UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(executable),WindowStyle=ProcessWindowStyle.Hidden };
                    int code;
                    if(launch!=null)code=launch(info);
                    else using(var p=Process.Start(info)) {if(p==null)throw new IOException("Windows не вернула процесс администратора.");p.WaitForExit();code=p.ExitCode;}
                    if(code!=0)throw new IOException("Системная операция не завершена (код "+code+"). Подробности показаны в окне администратора. При необходимости нажмите «Восстановить».");
                } catch(Win32Exception error) {
                    failure=error.NativeErrorCode==1223
                        ?(Exception)new OperationCanceledException("Запрос прав администратора отменён. Системная операция не запущена.",error)
                        :new IOException("Windows не смогла запустить помощник администратора (код 0x"+unchecked((uint)error.NativeErrorCode).ToString("X8")+"). Системный профиль не применён.\n\nЗакройте Open AntiLag через трей. Если EXE находится в OneDrive или синхронизируемой папке, перенесите его в локальную папку, например %LOCALAPPDATA%\\OpenAntiLag\\App, и запустите оттуда. Также можно проверить «Запуск от имени администратора». Если Windows покажет причину блокировки, сохраните её текст.\n\nФайл: "+executable,error);
                } catch(Exception error) {failure=error;}
            });
            thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
            if(failure!=null)ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
