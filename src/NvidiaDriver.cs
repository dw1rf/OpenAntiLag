using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenAntiLag {
    public sealed class NvidiaValue {
        public uint Value;
        public bool UserOverride;
    }
    public interface INvidiaDriver : IDisposable {
        string Version { get; }
        NvidiaValue Read(uint id);
        void Write(uint id,uint value);
        void Reset(uint id);
        void Save();
    }
    // NVDRS_SETTING_V1, pack(4). Layout verified against NVIDIA nvapi.h.
    [StructLayout(LayoutKind.Explicit,Size=12320,Pack=4)]
    struct DrsSetting {
        [FieldOffset(0)] public uint Version;
        [FieldOffset(4100)] public uint Id;
        [FieldOffset(4104)] public uint Type;
        [FieldOffset(4108)] public uint Location;
        [FieldOffset(4112)] public uint Predefined;
        [FieldOffset(8220)] public uint Value;
        public static DrsSetting Create(uint id) { return new DrsSetting { Version=12320U | (1U<<16),Id=id }; }
    }
    public sealed class NvidiaDriver : INvidiaDriver {
        // System32-only loading prevents an adjacent nvapi64.dll from being loaded.
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Init();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Create(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SessionCall(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ProfileCall(IntPtr session,out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Get(IntPtr session,IntPtr profile,uint id,ref DrsSetting setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Set(IntPtr session,IntPtr profile,ref DrsSetting setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Delete(IntPtr session,IntPtr profile,uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Ansi)] delegate int DriverInfo(out uint version,StringBuilder branch);
        // Extended entry points used by NVIDIA Profile Inspector (2f50c388); required
        // for non-public Control Panel IDs on newer drivers. Keep public ABI separate.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetExtended(IntPtr session,IntPtr profile,uint id,ref DrsSetting setting,ref uint extra);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetExtended(IntPtr session,IntPtr profile,ref DrsSetting setting,uint x,uint y);
        GetExtended getExtended;
        SetExtended setExtended;
        IntPtr library,session,profile;
        Query query;
        SessionCall destroy,save;
        Get get;
        Set set;
        Delete delete;
        public string Version { get; private set; }
        T Function<T>(uint id) where T:class {
            IntPtr p=query(id); if(p==IntPtr.Zero)throw new NotSupportedException("Драйвер NVIDIA не поддерживает нужную функцию NVAPI.");
            return Marshal.GetDelegateForFunctionPointer(p,typeof(T)) as T;
        }
        static void Check(int status,string operation) {
            if(status!=0)throw new InvalidOperationException(operation+": NVAPI "+status+(status==-137?". Доступ запрещён. Закройте приложение и запустите от администратора.":"."));
        }
        public NvidiaDriver() {
            try {
                library=LoadLibraryEx(Path.Combine(Environment.SystemDirectory,"nvapi64.dll"),IntPtr.Zero,0x800);
                if(library==IntPtr.Zero)throw new NotSupportedException("Драйвер NVIDIA/NVAPI не найден. Для AMD эта кнопка недоступна.");
                var entry=GetProcAddress(library,"nvapi_QueryInterface");
                if(entry==IntPtr.Zero)throw new NotSupportedException("NVAPI QueryInterface недоступен.");
                query=(Query)Marshal.GetDelegateForFunctionPointer(entry,typeof(Query));
                Check(Function<Init>(0x0150e828)(),"Инициализация NVIDIA");
                uint v; Check(Function<DriverInfo>(0x2926aaad)(out v,new StringBuilder(64)),"Версия драйвера");
                Version=(v/100)+"."+(v%100).ToString("00");
                destroy=Function<SessionCall>(0xdad9cff8); save=Function<SessionCall>(0xfcbc7e14);
                get=Function<Get>(0x73bf8338);set=Function<Set>(0x577dd202);delete=Function<Delete>(0xe4a26362);
                if(query(0xea99498d)!=IntPtr.Zero&&query(0x8a2cf5f5)!=IntPtr.Zero&&query(0xd20d29df)!=IntPtr.Zero) {
                    getExtended=Function<GetExtended>(0xea99498d);setExtended=Function<SetExtended>(0x8a2cf5f5);delete=Function<Delete>(0xd20d29df);
                }
                Check(Function<Create>(0x0694d52e)(out session),"Открытие профилей");
                Check(Function<SessionCall>(0x375dbd6b)(session),"Чтение профилей");
                Check(Function<ProfileCall>(0xda8466a0)(session,out profile),"Глобальный профиль");
                IntPtr current; Check(Function<ProfileCall>(0x617bff9f)(session,out current),"Активный глобальный профиль");
                if(current!=profile)throw new NotSupportedException("Активен нестандартный глобальный профиль NVIDIA. Выберите базовый профиль в панели NVIDIA.");
            } catch { Dispose(); throw; }
        }
        public NvidiaValue Read(uint id) {
            var value=DrsSetting.Create(id); uint extra=0; int status=getExtended==null?get(session,profile,id,ref value):getExtended(session,profile,id,ref value,ref extra);
            if(status==-160)return null;
            Check(status,"Чтение 0x"+id.ToString("X8"));
            if(value.Type!=0)throw new NotSupportedException("Неожиданный тип настройки 0x"+id.ToString("X8"));
            return new NvidiaValue { Value=value.Value,UserOverride=value.Location==0 && value.Predefined==0 };
        }
        public void Write(uint id,uint value) { var s=DrsSetting.Create(id);s.Value=value;Check(setExtended==null?set(session,profile,ref s):setExtended(session,profile,ref s,0,0),"Запись 0x"+id.ToString("X8")); }
        public void Reset(uint id) { Check(delete(session,profile,id),"Восстановление 0x"+id.ToString("X8")); }
        public void Save() { Check(save(session),"Сохранение NVIDIA"); }
        public void Dispose() { if(session!=IntPtr.Zero&&destroy!=null){destroy(session);session=IntPtr.Zero;} if(library!=IntPtr.Zero){FreeLibrary(library);library=IntPtr.Zero;} }
    }
}
