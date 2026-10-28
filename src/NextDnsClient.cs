using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace OpenAntiLag {
 public static class AtomicLocalFile {
  public static void Write(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));string temp=path+".tmp-"+Guid.NewGuid().ToString("N");try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
 public sealed class ApiKeyStore {
  readonly string path;static readonly byte[] Entropy=Encoding.UTF8.GetBytes("OpenAntiLag.NextDNS.v1");public ApiKeyStore(string path=null){this.path=path??Path.Combine(Program.DataDirectory,"nextdns.key");}
  public static void ValidateKey(string key){if(!Regex.IsMatch(key??"",@"^[A-Za-z0-9_.-]{8,512}$"))throw new ArgumentException("Некорректный API key: используйте ключ из аккаунта NextDNS без пробелов");}
  public void Save(string key){ValidateKey(key);AtomicLocalFile.Write(path,ProtectedData.Protect(Encoding.UTF8.GetBytes(key),Entropy,DataProtectionScope.CurrentUser));}
  public string Load(){if(!File.Exists(path))return null;if(new FileInfo(path).Length>8192)throw new InvalidDataException("Повреждён файл API key");try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),Entropy,DataProtectionScope.CurrentUser));}catch(CryptographicException){throw new InvalidDataException("API key недоступен для этой учётной записи. Сохраните его заново.");}}
  public void Delete(){if(File.Exists(path))File.Delete(path);if(File.Exists(path+".bak"))File.Delete(path+".bak");}
 }
 public sealed class NextDnsClient:IDisposable {
  readonly HttpClient client;readonly string key;readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=1024*1024};
  public NextDnsClient(string key,HttpMessageHandler handler=null){ApiKeyStore.ValidateKey(key);this.key=key;client=new HttpClient(handler??new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=1024*1024};ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;}
  public static string Profile(string id){if(!Regex.IsMatch(id??"",@"^[a-fA-F0-9]{6}$"))throw new ArgumentException("ID NextDNS: 6 шестнадцатеричных символов");return id.ToLowerInvariant();}
  public static string Domain(string value){string host=NetworkInput.Host(value);IPAddress ip;if(IPAddress.TryParse(host,out ip))throw new ArgumentException("Для списка нужен домен, а не IP");return host;}
  public async Task<string> Request(string method,string resource,string body,CancellationToken ct) {
   ct.ThrowIfCancellationRequested();
   if(!Regex.IsMatch(resource??"",@"^profiles(?:/[a-f0-9]{6}(?:/(?:allowlist|denylist)(?:/[a-zA-Z0-9.\-]+)?|/analytics/status|/logs\?limit=100)?)?$"))throw new ArgumentException("Запрещённый адрес API");
   if(method!="GET"&&!Regex.IsMatch(resource??"",@"^profiles/[a-f0-9]{6}/(?:allowlist|denylist)(?:/[a-zA-Z0-9.\-]+)?$"))throw new ArgumentException("Изменение целого профиля не поддерживается");
   if(method!="GET"&&method!="POST"&&method!="DELETE")throw new ArgumentException("Запрещённый метод API");
   using(var request=new HttpRequestMessage(new HttpMethod(method),"https://api.nextdns.io/"+resource)) {
    request.Headers.Add("X-Api-Key",key);if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
    try {using(var response=await client.SendAsync(request,ct)){if(!response.IsSuccessStatusCode)throw new IOException("NextDNS API: HTTP "+(int)response.StatusCode+". Проверьте права ключа и доступность сервиса.");string text=await response.Content.ReadAsStringAsync();ct.ThrowIfCancellationRequested();var data=json.DeserializeObject(text) as System.Collections.Generic.Dictionary<string,object>;if(data==null||data.ContainsKey("errors"))throw new IOException("NextDNS отклонил запрос. Проверьте домен, профиль и права ключа.");return text.Replace(key,"[API key hidden]");}}
    catch(OperationCanceledException){ct.ThrowIfCancellationRequested();throw new TimeoutException("NextDNS API не ответил вовремя");}catch(HttpRequestException){throw new IOException("NextDNS API недоступен; проверьте подключение");}catch(ArgumentException){throw new IOException("Некорректный ответ NextDNS API");}
   }
  }
  public Task<string> Profiles(CancellationToken ct){return Request("GET","profiles",null,ct);}
  public Task<string> List(string profile,string list,CancellationToken ct){if(list!="allowlist"&&list!="denylist")throw new ArgumentException();return Request("GET","profiles/"+Profile(profile)+"/"+list,null,ct);}
  public Task<string> Change(string profile,string list,string domain,bool remove,CancellationToken ct){if(list!="allowlist"&&list!="denylist")throw new ArgumentException();domain=Domain(domain);return Request(remove?"DELETE":"POST","profiles/"+Profile(profile)+"/"+list+(remove?"/"+domain:""),remove?null:json.Serialize(new {id=domain,active=true}),ct);}
  public Task<string> PrivateData(string profile,bool logs,CancellationToken ct){return Request("GET","profiles/"+Profile(profile)+(logs?"/logs?limit=100":"/analytics/status"),null,ct);}
  public void Dispose(){client.Dispose();}
 }
}
