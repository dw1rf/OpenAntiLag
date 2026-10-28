using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
namespace OpenAntiLag {
 public sealed class AdapterSnapshot {
  public string Id,Name,Type,State;public int Index4,Index6,Mtu4,Mtu6;public long Speed,Received,Sent;public string[] Addresses=new string[0],Dns=new string[0],Gateways=new string[0];
  public override string ToString(){return Name+" · "+Type+" · "+State;}
 }
 public static class NetworkAdapters {
  [DllImport("iphlpapi.dll")]static extern uint GetBestInterface(uint destination,out uint index);
  public static string Route(IPAddress destination,AdapterSnapshot[] adapters){if(destination.AddressFamily!=AddressFamily.InterNetwork)return "IPv6 route: не определяется этой версией";uint index;if(GetBestInterface(BitConverter.ToUInt32(destination.GetAddressBytes(),0),out index)!=0)return "Нет определённого IPv4 маршрута";var adapter=adapters.FirstOrDefault(a=>a.Index4==index);return adapter==null?"IPv4 ifIndex "+index:adapter.Id+" ("+adapter.Name+")";}

  public static AdapterSnapshot[] Read() {
   var list=new List<AdapterSnapshot>();foreach(var n in NetworkInterface.GetAllNetworkInterfaces()) {
    var a=new AdapterSnapshot {Id=n.Id,Name=n.Name,Type=n.NetworkInterfaceType.ToString(),State=n.OperationalStatus.ToString(),Speed=n.Speed};
    try{var p=n.GetIPProperties();a.Addresses=p.UnicastAddresses.Select(x=>x.Address.ToString()).ToArray();a.Dns=p.DnsAddresses.Select(x=>x.ToString()).ToArray();a.Gateways=p.GatewayAddresses.Where(x=>!x.Address.Equals(IPAddress.Any)&&!x.Address.Equals(IPAddress.IPv6Any)).Select(x=>x.Address.ToString()).ToArray();try{a.Index4=p.GetIPv4Properties().Index;a.Mtu4=p.GetIPv4Properties().Mtu;}catch(NetworkInformationException){}try{a.Index6=p.GetIPv6Properties().Index;a.Mtu6=p.GetIPv6Properties().Mtu;}catch(NetworkInformationException){}var stats=n.GetIPStatistics();a.Received=stats.BytesReceived;a.Sent=stats.BytesSent;}catch(NetworkInformationException){}
    list.Add(a);
   }return list.ToArray();
  }
  public static string Signature(IEnumerable<AdapterSnapshot> adapters){return String.Join(";",adapters.Where(a=>a.State=="Up").OrderBy(a=>a.Id).Select(a=>a.Id+":"+String.Join(",",a.Addresses)+":"+String.Join(",",a.Gateways)));}
  public static string Describe(AdapterSnapshot a){return a+"\r\n  IPv4/IPv6: "+String.Join(", ",a.Addresses)+"\r\n  MTU: v4="+a.Mtu4+" v6="+a.Mtu6+" · скорость линка "+(a.Speed/1000000)+" Мбит/с\r\n  Шлюзы: "+String.Join(", ",a.Gateways)+" · DNS: "+String.Join(", ",a.Dns);}
 }
 public interface IProbeTransport {Task<ProbeSample> Ping(IPAddress address,int timeout,CancellationToken ct);Task<ProbeSample> Tcp(IPAddress address,int port,int timeout,CancellationToken ct);}
 public sealed class SystemProbeTransport:IProbeTransport {
  public async Task<ProbeSample> Ping(IPAddress address,int timeout,CancellationToken ct){ct.ThrowIfCancellationRequested();var result=new ProbeSample {Address=address.ToString(),Sent=true};try{using(var ping=new Ping()){var reply=await ping.SendPingAsync(address,timeout);ct.ThrowIfCancellationRequested();result.Success=reply.Status==IPStatus.Success;result.Milliseconds=reply.RoundtripTime;result.Detail=reply.Status.ToString();}}catch(PingException){result.Sent=false;result.Detail="ICMP недоступен или запрещён";}catch(SocketException){result.Sent=false;result.Detail="Сетевая ошибка ICMP";}return result;}
  public async Task<ProbeSample> Tcp(IPAddress address,int port,int timeout,CancellationToken ct){var result=new ProbeSample {Address=address.ToString(),Kind="TCP connect",Sent=true};var watch=Stopwatch.StartNew();using(var client=new TcpClient(address.AddressFamily)){try{await NetworkInput.Bounded(client.ConnectAsync(address,port).ContinueWith(t=>{t.GetAwaiter().GetResult();return true;}),timeout,ct);result.Success=true;result.Detail="TCP "+port+" доступен; не измерение игрового UDP";}catch(SocketException e){result.Detail="TCP: "+e.SocketErrorCode+"; это не packet loss";}catch(TimeoutException){result.Detail="TCP timeout; это не packet loss";}result.Milliseconds=watch.Elapsed.TotalMilliseconds;}return result;}
 }
 public sealed class MonitorOptions {
  public string Host;public bool IncludeGateway,IncludeDns,TcpFallback;public int IntervalMs=2000,DurationSeconds=120;
  public void Validate(){Host=NetworkInput.Host(Host);if(IntervalMs<1000||IntervalMs>60000||DurationSeconds<5||DurationSeconds>3600||IntervalMs>DurationSeconds*1000)throw new ArgumentException("Интервал 1–60 секунд, сеанс 5–3600 секунд.");}
 }
 public sealed class NetworkMonitor {
  readonly IProbeTransport transport;readonly Func<AdapterSnapshot[]> adapters;public readonly ObservationHistory History=new ObservationHistory();
  public event Action<ProbeSample> Sample;public event Action<string> Notice;public NetworkMonitor(IProbeTransport transport=null,Func<AdapterSnapshot[]> adapters=null){this.transport=transport??new SystemProbeTransport();this.adapters=adapters??NetworkAdapters.Read;}
  void Say(string value){var handler=Notice;if(handler!=null)handler(value);}
  void Add(ProbeSample sample){History.Add(sample);var handler=Sample;if(handler!=null)handler(sample);}
  int running;public bool IsRunning {get{return Interlocked.CompareExchange(ref running,0,0)!=0;}}
  public async Task Run(MonitorOptions options,CancellationToken ct){if(Interlocked.CompareExchange(ref running,1,0)!=0)throw new InvalidOperationException("Диагностика уже выполняется. Завершите текущую проверку.");try{History.Clear();await RunCore(options,ct);}finally{Interlocked.Exchange(ref running,0);}}
  async Task RunCore(MonitorOptions options,CancellationToken ct) {
   ct.ThrowIfCancellationRequested();options.Validate();var host=options.Host;IPAddress destination;
   if(!IPAddress.TryParse(host,out destination)){try{destination=(await NetworkInput.Bounded(Dns.GetHostAddressesAsync(host),3000,ct)).FirstOrDefault();if(destination==null)throw new SocketException();}catch(OperationCanceledException){throw;}catch(Exception){Say("DNS: не удалось разрешить выбранный хост. ICMP не отправлялся.");Add(new ProbeSample {Target=host,Kind="DNS",Detail="Сбой системного DNS; локальный кэш может участвовать"});return;}}
   string lastSignature=null,lastRoute=null;int iteration=0,failures=0;var duration=Stopwatch.StartNew();
   while(duration.Elapsed.TotalSeconds<options.DurationSeconds) {
    ct.ThrowIfCancellationRequested();var cycle=Stopwatch.StartNew();var all=await Task.Run(()=>adapters(),ct);string signature=NetworkAdapters.Signature(all);if(lastSignature!=null&&signature!=lastSignature)Say("Факт: изменились адреса/состояния активных интерфейсов. Статистика RTT может быть несопоставима.");lastSignature=signature;
    string route=NetworkAdapters.Route(destination,all);if(lastRoute!=route){Say("Факт: маршрут Windows к выбранному адресу: "+route);lastRoute=route;}
    var live=all.Where(a=>a.State=="Up"&&a.Type!="Loopback").ToArray();if(live.Length==0)Say("Факт: нет работающего сетевого интерфейса. Возможен разрыв локального подключения.");
    var targets=new List<Tuple<string,IPAddress>> {Tuple.Create(host,destination)};
    if(options.IncludeGateway)foreach(var a in live.Take(2))foreach(var g in a.Gateways.Take(1)){IPAddress ip;if(IPAddress.TryParse(g,out ip))targets.Add(Tuple.Create("Шлюз "+a.Name,ip));}
    if(options.IncludeDns)foreach(var server in live.SelectMany(a=>a.Dns).Distinct().Take(2)){IPAddress ip;if(IPAddress.TryParse(server,out ip))targets.Add(Tuple.Create("DNS-сервер (ICMP) "+server,ip));}
    var probes=targets.Take(5).Select(async t=>{var result=await transport.Ping(t.Item2,800,ct);ct.ThrowIfCancellationRequested();result.Target=t.Item1;result.InterfaceId=signature+" | "+route;result.ReceivedBytes=live.Sum(a=>a.Received);result.SentBytes=live.Sum(a=>a.Sent);Add(result);return result;}).ToArray();var results=await Task.WhenAll(probes);ct.ThrowIfCancellationRequested();
    if(!results[0].Success){failures++;if(failures==3)Say("Признак: 3 диагностические ICMP-пробы без ответа. Возможны фильтрация ICMP или недоступность адреса; потеря игрового UDP не доказана.");if(options.TcpFallback&&iteration%10==0){var tcp=await transport.Tcp(destination,443,1200,ct);tcp.Target=host;tcp.InterfaceId=signature+" | "+route;Add(tcp);}}else if(failures>0){Say("Факт: ответы ICMP возобновились.");failures=0;}
    if(options.IncludeDns&&iteration%5==0)foreach(string server in live.SelectMany(a=>a.Dns).Distinct().Take(2)){var dnsResult=await DnsProbe.Query(server,"example.com",ct);ct.ThrowIfCancellationRequested();Add(new ProbeSample {Target="DNS "+server,Address=server,Kind="DNS",Sent=true,Success=dnsResult.Success,Milliseconds=dnsResult.Milliseconds,InterfaceId=signature+" | "+route,Detail=dnsResult.Success?"example.com: "+String.Join(",",dnsResult.Answers):dnsResult.Error});}
    iteration++;int wait=Math.Max(1,options.IntervalMs-(int)cycle.ElapsedMilliseconds);if(duration.Elapsed.TotalSeconds+wait/1000.0>=options.DurationSeconds)break;await Task.Delay(wait,ct);
   }
  }
 }
 public static class NetworkDiagnostics {
  public static string Analyze(ProbeSample[] samples,AdapterSnapshot[] adapters,double rttThreshold,double jitterThreshold) {
   var lines=new List<string>();var live=adapters.Where(a=>a.State=="Up"&&a.Type!="Loopback").ToArray();if(live.Length==0)lines.Add("Факт: нет активных интерфейсов. Проверьте кабель/Wi-Fi и состояние адаптера.");if(live.Length>1)lines.Add("Факт: активны несколько интерфейсов. Windows выбирает маршрут; объединение каналов не выполняется.");
   foreach(var group in samples.Where(s=>s.Kind=="ICMP").GroupBy(s=>s.Target)){var summary=ProbeSummary.Calculate(group);if(summary.P95>rttThreshold)lines.Add("Признак: высокий P95 RTT к "+group.Key+". Сравните этот же адрес по кабелю и в другое время.");if(summary.Jitter>jitterThreshold)lines.Add("Признак: высокая вариация RTT к "+group.Key+". Сопоставьте с загрузкой адаптера; это не доказывает виновность фонового приложения.");if(summary.Lost>0&&group.Key.StartsWith("Шлюз"))lines.Add("Гипотеза: проблема локального участка или фильтрация ICMP шлюзом. Проверьте Ethernet/Wi-Fi отдельно.");}
   foreach(var group in samples.Where(s=>s.Kind=="ICMP").GroupBy(s=>s.Target)) {
    var ordered=group.OrderBy(s=>s.Utc).ToArray();var pairs=new List<Tuple<double,double>>();
    for(int i=1;i<ordered.Length;i++){var a=ordered[i-1];var b=ordered[i];double seconds=(b.Utc-a.Utc).TotalSeconds;long bytes=b.ReceivedBytes+b.SentBytes-a.ReceivedBytes-a.SentBytes;if(a.Success&&b.Success&&a.InterfaceId==b.InterfaceId&&seconds>0&&bytes>=0)pairs.Add(Tuple.Create(b.Milliseconds,bytes/seconds));}
    if(pairs.Count>=5){double x=pairs.Average(p=>p.Item1),y=pairs.Average(p=>p.Item2);double xx=pairs.Sum(p=>Math.Pow(p.Item1-x,2)),yy=pairs.Sum(p=>Math.Pow(p.Item2-y,2));if(xx>0&&yy>0){double correlation=pairs.Sum(p=>(p.Item1-x)*(p.Item2-y))/Math.Sqrt(xx*yy);lines.Add("Наблюдение: корреляция RTT и суммы счётчиков трафика адаптеров к "+group.Key+" r="+correlation.ToString("0.00")+" (n="+pairs.Count+"). Это не причинность; виртуальные адаптеры могут дублировать трафик. По процессам скорость не измерялась.");}}
   }
   if(samples.Any(s=>s.Kind=="DNS"&&!s.Success))lines.Add("Признак: ошибка разрешения DNS. Сравните запрос к настроенному DNS и к выбранному альтернативному серверу.");if(samples.Select(s=>s.InterfaceId).Where(s=>s!=null).Distinct().Count()>1)lines.Add("Факт: интерфейсы/адреса менялись в ходе сеанса. Сравнивайте интервалы по отдельности.");if(lines.Count==0)lines.Add("По собранным данным причина не установлена. Отсутствие предупреждений не доказывает отсутствие проблем в игре.");return String.Join("\r\n",lines);
  }
 }
}
