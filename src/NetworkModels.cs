using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace OpenAntiLag {
 public sealed class ProbeSample {
  public DateTime Utc=DateTime.UtcNow;public string Target,Address,Kind="ICMP",InterfaceId,Detail;public bool Sent,Success;public double Milliseconds;public long ReceivedBytes,SentBytes;
 }
 public sealed class ProbeSummary {
  public int Sent,Received,Lost;public double? Current,Minimum,Maximum,Mean,Median,P95,Jitter,StandardDeviation;
  public double LossPercent {get{return Sent==0?0:100.0*Lost/Sent;}}
  public static double Percentile(double[] sorted,double fraction) {if(sorted.Length==0)throw new ArgumentException();double i=(sorted.Length-1)*fraction;int lo=(int)Math.Floor(i),hi=(int)Math.Ceiling(i);return sorted[lo]+(sorted[hi]-sorted[lo])*(i-lo);}
  public static ProbeSummary Calculate(IEnumerable<ProbeSample> source) {
   var samples=source.Where(x=>x.Kind=="ICMP"&&x.Sent).ToArray();var ok=samples.Where(x=>x.Success).ToArray();var result=new ProbeSummary {Sent=samples.Length,Received=ok.Length,Lost=samples.Length-ok.Length};
   if(ok.Length==0)return result;var values=ok.Select(x=>x.Milliseconds).OrderBy(x=>x).ToArray();result.Current=ok.Last().Milliseconds;result.Minimum=values.First();result.Maximum=values.Last();result.Mean=values.Average();result.Median=Percentile(values,.5);result.P95=Percentile(values,.95);result.StandardDeviation=Math.Sqrt(values.Average(x=>Math.Pow(x-result.Mean.Value,2)));
   var differences=new List<double>();for(int i=1;i<samples.Length;i++)if(samples[i].Success&&samples[i-1].Success&&samples[i].InterfaceId==samples[i-1].InterfaceId&&samples[i].Address==samples[i-1].Address)differences.Add(Math.Abs(samples[i].Milliseconds-samples[i-1].Milliseconds));
   if(differences.Count>0)result.Jitter=differences.Average();return result;
  }
  static string N(double? value){return value.HasValue?value.Value.ToString("0.0",CultureInfo.InvariantCulture):"—";}
  public override string ToString(){return "RTT мс: сейчас "+N(Current)+" · min "+N(Minimum)+" · max "+N(Maximum)+" · avg "+N(Mean)+" · median "+N(Median)+" · P95 "+N(P95)+"\r\nJitter |ΔRTT|: "+N(Jitter)+" мс · σ RTT: "+N(StandardDeviation)+" мс\r\nICMP: отправлено "+Sent+", ответов "+Received+", без ответа "+Lost+" ("+LossPercent.ToString("0.0",CultureInfo.InvariantCulture)+"%). Это не потери игровых UDP-пакетов.";}
 }
 public static class NetworkInput {
  public static string Host(string value) {value=(value??"").Trim();IPAddress ip;if(IPAddress.TryParse(value,out ip))return ip.ToString();if(value.Length<1||value.Length>253)throw new ArgumentException("Укажите IP или доменное имя без URL/порта.");value=new IdnMapping().GetAscii(value.TrimEnd('.'));if(value.Split('.').Any(p=>p.Length==0||p.Length>63||p[0]=='-'||p[p.Length-1]=='-'||p.Any(c=>!Char.IsLetterOrDigit(c)&&c!='-')))throw new ArgumentException("Некорректное доменное имя.");return value.ToLowerInvariant();}
  public static string[] Servers(string value,int family) {var result=(value??"").Split(new[]{',',';',' ','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>{IPAddress ip;if(!IPAddress.TryParse(x,out ip)||(family==4)!=(ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)||IPAddress.Any.Equals(ip)||IPAddress.IPv6Any.Equals(ip))throw new ArgumentException("Нужны корректные DNS-адреса IPv"+family);return ip.ToString();}).ToArray();if(result.Length<1||result.Length>4||result.Distinct().Count()!=result.Length)throw new ArgumentException("Укажите от 1 до 4 различных DNS-серверов.");return result;}
  static void ObserveFault(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);}
  public static async Task<T> Bounded<T>(Task<T> task,int milliseconds,CancellationToken ct) {
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct)){var wait=Task.Delay(milliseconds,deadline.Token);if(await Task.WhenAny(task,wait)!=task){ct.ThrowIfCancellationRequested();ObserveFault(task);throw new TimeoutException("Время ожидания истекло.");}deadline.Cancel();return await task;}
  }
 }
 public sealed class ObservationHistory {
  readonly object gate=new object();readonly List<ProbeSample> items=new List<ProbeSample>();public const int Limit=10000;
  public void Add(ProbeSample item){lock(gate){items.Add(item);if(items.Count>Limit)items.RemoveRange(0,items.Count-Limit);}}
  public ProbeSample[] Snapshot(){lock(gate)return items.ToArray();}
  public void Clear(){lock(gate)items.Clear();}
  public static string Csv(IEnumerable<ProbeSample> source){var b=new StringBuilder("utc,target,address,kind,sent,success,milliseconds,interface,detail,adapter_rx_bytes,adapter_tx_bytes\r\n");foreach(var s in source)b.AppendLine(String.Join(",",new[]{s.Utc.ToString("o"),s.Target,s.Address,s.Kind,s.Sent.ToString(),s.Success.ToString(),s.Milliseconds.ToString("0.###",CultureInfo.InvariantCulture),s.InterfaceId,s.Detail,s.ReceivedBytes.ToString(CultureInfo.InvariantCulture),s.SentBytes.ToString(CultureInfo.InvariantCulture)}.Select(CsvCell)));return b.ToString();}
  public static string CsvCell(string text){text=text??"";if(text.Length>0&&"=+-@\t\r".Contains(text[0]))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
  public static void Export(string path,ProbeSample[] samples){File.WriteAllText(path,Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase)?Csv(samples):new JavaScriptSerializer {MaxJsonLength=16*1024*1024}.Serialize(new {Schema=1,Note="ICMP diagnostic observations; not game packet loss",Samples=samples}),new UTF8Encoding(true));}
 }
}
