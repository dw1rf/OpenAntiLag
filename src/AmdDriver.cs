using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenAntiLag {
    public sealed class AmdGpu {public string Id,Name;public override string ToString(){return Name;}}
    public interface IAmdDriver:IDisposable {List<AmdGpu> Devices {get;} void Select(string id); int? Read(string key);void Write(string key,int value);}
    // ADLX C ABI, ISystem.h and I3DSettings.h, SDK commit 32b5a740.
    public sealed class AmdDriver:IAmdDriver {
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int VersionFn(out ulong version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn(ulong version,out IntPtr system);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TermFn();
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RefFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PtrFn(IntPtr self,out IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint SizeFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int AtFn(IntPtr self,uint index,out IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FeatureFn(IntPtr self,IntPtr gpu,out IntPtr output);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BoolGet(IntPtr self,out byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BoolSet(IntPtr self,byte value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntGet(IntPtr self,out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntSet(IntPtr self,int value);
        IntPtr library,system,services,list;
        TermFn terminate;
        bool initialized;
        readonly Dictionary<string,IntPtr> gpus=new Dictionary<string,IntPtr>();
        readonly Dictionary<string,IntPtr> features=new Dictionary<string,IntPtr>();
        public List<AmdGpu> Devices {get;private set;}
        static T Slot<T>(IntPtr p,int index) where T:class {if(p==IntPtr.Zero)throw new InvalidOperationException("Пустой интерфейс ADLX.");return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p),index*IntPtr.Size),typeof(T)) as T;}
        T Export<T>(string name) where T:class {var p=GetProcAddress(library,name);if(p==IntPtr.Zero)throw new NotSupportedException("Нет функции ADLX: "+name);return Marshal.GetDelegateForFunctionPointer(p,typeof(T)) as T;}
        static void Check(int code,string action){if(code<0||code>2)throw new InvalidOperationException(action+": ADLX "+code+". Проверьте драйвер AMD Adrenalin.");}
        static void Release(IntPtr p){if(p!=IntPtr.Zero)Slot<RefFn>(p,1)(p);}
        static string Text(IntPtr p,int slot){IntPtr s;Check(Slot<PtrFn>(p,slot)(p,out s),"Сведения GPU");return Marshal.PtrToStringAnsi(s);}
        public AmdDriver() {
            Devices=new List<AmdGpu>();
            try {
                library=LoadLibraryEx(Path.Combine(Environment.SystemDirectory,"amdadlx64.dll"),IntPtr.Zero,0x800);
                if(library==IntPtr.Zero)throw new NotSupportedException("AMD ADLX не найден. Нужны совместимая Radeon и установленный AMD Software: Adrenalin. На NVIDIA эти параметры не применяются.");
                ulong version;Check(Export<VersionFn>("ADLXQueryFullVersion")(out version),"Версия ADLX");
                if((version>>48)<1||(version>>48)>2)throw new NotSupportedException("Эта версия ADLX ещё не поддерживается приложением.");
                terminate=Export<TermFn>("ADLXTerminate");int init=Export<InitFn>("ADLXInitialize")(version,out system);Check(init,"Запуск ADLX");initialized=init!=2;
                Check(Slot<PtrFn>(system,1)(system,out list),"Список Radeon");
                uint count=Slot<SizeFn>(list,3)(list);if(count>32)throw new InvalidDataException("Некорректный список Radeon.");
                for(uint i=0;i<count;i++){IntPtr gpu;Check(Slot<AtFn>(list,11)(list,i,out gpu),"Чтение Radeon");try {string id=Text(gpu,9),name=Text(gpu,7);if(String.IsNullOrWhiteSpace(id)||gpus.ContainsKey(id))throw new InvalidDataException("Невозможно однозначно определить Radeon.");gpus.Add(id,gpu);Devices.Add(new AmdGpu {Id=id,Name=name});gpu=IntPtr.Zero;}finally{Release(gpu);}}
                if(Devices.Count==0)throw new NotSupportedException("Совместимая видеокарта AMD не обнаружена.");
                Check(Slot<PtrFn>(system,7)(system,out services),"Глобальная графика AMD");
            }catch{Dispose();throw;}
        }
        public void Select(string id){foreach(var f in features.Values)Release(f);features.Clear();IntPtr gpu;if(!gpus.TryGetValue(id,out gpu))throw new InvalidOperationException("Выбранная Radeon больше не подключена.");
            int[] slots={3,4,5,7,11,8};string[] keys={"antilag","chill","boost","enhanced","morphological","vsync"};
            for(int i=0;i<keys.Length;i++){IntPtr f;int result=Slot<FeatureFn>(services,slots[i])(services,gpu,out f);if(result==12||result==6){Release(f);continue;}try {Check(result,"Чтение "+keys[i]);byte supported;Check(Slot<BoolGet>(f,3)(f,out supported),"Поддержка "+keys[i]);if(supported!=0){features.Add(keys[i],f);f=IntPtr.Zero;}}finally{Release(f);}}
        }
        public int? Read(string key){IntPtr f;if(!features.TryGetValue(key,out f))return null;if(key=="vsync"){int v;Check(Slot<IntGet>(f,5)(f,out v),"Чтение V-Sync");return v;}byte b;Check(Slot<BoolGet>(f,4)(f,out b),"Чтение "+key);return b==0?0:1;}
        public void Write(string key,int value){IntPtr f;if(!features.TryGetValue(key,out f))throw new NotSupportedException("Параметр AMD недоступен: "+key);AmdProfiles.ValidateValue(key,value);if(key=="vsync")Check(Slot<IntSet>(f,6)(f,value),"Запись V-Sync");else Check(Slot<BoolSet>(f,key=="chill"?8:key=="boost"?7:5)(f,(byte)value),"Запись "+key);}
        public void Dispose(){foreach(var f in features.Values)Release(f);features.Clear();Release(services);services=IntPtr.Zero;foreach(var g in gpus.Values)Release(g);gpus.Clear();Release(list);list=IntPtr.Zero;if(initialized){terminate();initialized=false;}system=IntPtr.Zero;if(library!=IntPtr.Zero){FreeLibrary(library);library=IntPtr.Zero;}}
    }
}
