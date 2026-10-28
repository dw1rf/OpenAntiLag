using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace OpenAntiLag {
 public sealed class PerformanceSample {public DateTime Utc=DateTime.UtcNow;public double? CpuPercent,ProcessCpuPercent;public ulong RamTotal,RamAvailable;public long? ProcessWorkingSet;public int ProcessId;public string Note;}
 public sealed class PerformanceMonitor {
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
  [StructLayout(LayoutKind.Sequential)] sealed class Memory {public uint Length=(uint)Marshal.SizeOf(typeof(Memory));public uint Load;public ulong TotalPhys,AvailPhys,TotalPage,AvailPage,TotalVirtual,AvailVirtual,Extended;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx([In,Out] Memory memory);
  readonly List<PerformanceSample> history=new List<PerformanceSample>();readonly object gate=new object();
  public bool Completed {get;private set;}
  public event Action<PerformanceSample> Sample;public PerformanceSample[] Snapshot(){lock(gate)return history.ToArray();}
  public static double? CpuDelta(long idle0,long kernel0,long user0,long idle1,long kernel1,long user1){long total=kernel1-kernel0+user1-user0,idle=idle1-idle0;if(total<=0||idle<0||idle>total)return null;return Math.Max(0,Math.Min(100,100.0*(total-idle)/total));}
  public async Task Run(int processId,int duration,int interval,CancellationToken ct){Completed=false;if(duration<5||duration>3600||interval<1000||interval>60000||interval>duration*1000)throw new ArgumentException();long i0,k0,u0;bool previous=GetSystemTimes(out i0,out k0,out u0);double? process0=null;long? started=null;var watch=Stopwatch.StartNew();double previousMs=watch.Elapsed.TotalMilliseconds;
   while(watch.Elapsed.TotalSeconds<duration){await Task.Delay(interval,ct);ct.ThrowIfCancellationRequested();var sample=new PerformanceSample {ProcessId=processId};long idle,kernel,user;bool read=GetSystemTimes(out idle,out kernel,out user);if(previous&&read)sample.CpuPercent=CpuDelta(i0,k0,u0,idle,kernel,user);previous=read;i0=idle;k0=kernel;u0=user;var memory=new Memory();if(GlobalMemoryStatusEx(memory)){sample.RamTotal=memory.TotalPhys;sample.RamAvailable=memory.AvailPhys;}else sample.Note="RAM API недоступен";
    double now=watch.Elapsed.TotalMilliseconds;if(processId>0)try{using(var process=Process.GetProcessById(processId)){long start=process.StartTime.ToUniversalTime().Ticks;if(started.HasValue&&started.Value!=start){throw new IOException("PID был использован другим процессом; измерение не завершено");}started=start;double total=process.TotalProcessorTime.TotalMilliseconds;sample.ProcessWorkingSet=process.WorkingSet64;if(process0.HasValue)sample.ProcessCpuPercent=Math.Max(0,Math.Min(100,100*(total-process0.Value)/Math.Max(1,now-previousMs)/Environment.ProcessorCount));process0=total;}}catch(ArgumentException){throw new IOException("Выбранный процесс завершился; измерение не завершено");}catch(System.ComponentModel.Win32Exception){sample.Note="Метрики процесса недоступны по правам";process0=null;}catch(InvalidOperationException){throw new IOException("Выбранный процесс завершился; измерение не завершено");}previousMs=now;lock(gate){history.Add(sample);if(history.Count>10000)history.RemoveAt(0);}var handler=Sample;if(handler!=null)handler(sample);
   }
   Completed=true;
  }
  public static void Export(string path,PerformanceSample[] samples){if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase)){var b=new StringBuilder("utc,cpu_percent,process_cpu_percent,ram_total,ram_available,working_set,pid\r\n");foreach(var s in samples)b.AppendLine(String.Join(",",new[]{s.Utc.ToString("o"),s.CpuPercent.HasValue?s.CpuPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"",s.ProcessCpuPercent.HasValue?s.ProcessCpuPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"",s.RamTotal.ToString(),s.RamAvailable.ToString(),s.ProcessWorkingSet.ToString(),s.ProcessId.ToString()}.Select(ObservationHistory.CsvCell)));File.WriteAllText(path,b.ToString(),new UTF8Encoding(true));}else File.WriteAllText(path,new JavaScriptSerializer {MaxJsonLength=16*1024*1024}.Serialize(samples),new UTF8Encoding(true));}
 }
 public sealed class BenchmarkResult {public int Duration,Interval,ProcessId;public PerformanceSample[] Samples;public string Label;}
 public static class BenchmarkComparison {
  public static string Compare(BenchmarkResult before,BenchmarkResult after){if(before==null||after==null)throw new ArgumentException("Сначала выполните оба измерения");if(before.Duration!=after.Duration||before.Interval!=after.Interval||before.ProcessId!=after.ProcessId)throw new ArgumentException("Для сравнения нужны одинаковые длительность, интервал и выбранный процесс");if(before.Samples.Length<2||after.Samples.Length<2)throw new ArgumentException("Недостаточно данных");Func<PerformanceSample[],double?> average=s=>{var valid=s.Where(x=>x.CpuPercent.HasValue).Select(x=>x.CpuPercent.Value).ToArray();return valid.Length==0?(double?)null:valid.Average();};return "Средняя CPU: до "+average(before.Samples)+"%, после "+average(after.Samples)+"%.\r\nЭто наблюдение, а не доказанный прирост FPS. Игра, сцена, фоновые задачи и длительность должны совпадать. FPS/1% Low/frametime здесь не измеряются.";}
 }
 public sealed class GameNetworkProfile {
  public string Name,ProcessName;public string DiagnosticHost="1.1.1.1";public bool IncludeGateway,IncludeDns,TcpFallback;public int IntervalMs=2000,DurationSeconds=120;public double HighRttMs=100,HighJitterMs=20;public string[] Exclusions=new string[0];public override string ToString(){return Name;}
 }
 public static class GameProfileManager {
  public static GameNetworkProfile[] Defaults(){return new[]{new GameNetworkProfile {Name="CS2 / FACEIT — мониторинг",ProcessName="cs2",Exclusions=new[]{"faceitclient","faceitservice"}},new GameNetworkProfile {Name="Универсальный игровой",ProcessName=""},new GameNetworkProfile {Name="Пользовательский",ProcessName=""}};}
  public static void Validate(GameNetworkProfile value){if(value==null||String.IsNullOrWhiteSpace(value.Name)||value.Name.Length>80||value.ProcessName==null||value.ProcessName.Length>80||value.ProcessName.Any(c=>!Char.IsLetterOrDigit(c)&&c!='-'&&c!='_')||value.IntervalMs<1000||value.IntervalMs>60000||value.DurationSeconds<5||value.DurationSeconds>3600||value.HighRttMs<1||value.HighRttMs>10000||value.HighJitterMs<1||value.HighJitterMs>10000||value.Exclusions==null||value.Exclusions.Length>100||value.Exclusions.Any(x=>String.IsNullOrWhiteSpace(x)||x.Length>80))throw new InvalidDataException("Некорректный профиль мониторинга");NetworkInput.Host(value.DiagnosticHost);}
  public static void Save(string path,GameNetworkProfile value){Validate(value);AtomicLocalFile.Write(path,Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)));}
  public static GameNetworkProfile Load(string path){if(new FileInfo(path).Length>65536)throw new InvalidDataException();var value=new JavaScriptSerializer().Deserialize<GameNetworkProfile>(File.ReadAllText(path));Validate(value);return value;}
 }
}
