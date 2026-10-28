using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace OpenAntiLag {
 public sealed class DnsMeasurement {public string Server,Host,Error;public bool Success;public double Milliseconds;public string[] Answers=new string[0];}
 public static class DnsProbe {
  static int U16(byte[] b,int i){if(i<0||i+2>b.Length)throw new FormatException("Короткий DNS-пакет");return b[i]*256+b[i+1];}
  public static byte[] Question(string host,ushort id){host=NetworkInput.Host(host);IPAddress ip;if(IPAddress.TryParse(host,out ip))throw new ArgumentException("Для DNS-теста укажите домен.");var bytes=new List<byte>{(byte)(id>>8),(byte)id,1,0,0,1,0,0,0,0,0,0};foreach(var label in host.Split('.')){var part=Encoding.ASCII.GetBytes(label);bytes.Add((byte)part.Length);bytes.AddRange(part);}bytes.AddRange(new byte[]{0,0,1,0,1});return bytes.ToArray();}
  static void SkipName(byte[] bytes,ref int pos){int count=0;while(true){if(pos>=bytes.Length||++count>128)throw new FormatException("Некорректное DNS-имя");int length=bytes[pos++];if(length==0)return;if((length&0xc0)==0xc0){if(pos>=bytes.Length)throw new FormatException();pos++;return;}if(length>63||pos+length>bytes.Length)throw new FormatException();pos+=length;}}
  public static string[] Answer(byte[] bytes,ushort id,byte[] question){if(bytes.Length<12||U16(bytes,0)!=id||(bytes[2]&0x80)==0||U16(bytes,4)!=1||(bytes[2]&0x78)!=0)throw new FormatException("Ответ DNS не соответствует запросу");if((bytes[2]&2)!=0)throw new FormatException("DNS-ответ усечён: нужен TCP; результат не засчитан");if((bytes[3]&15)!=0)throw new FormatException("DNS RCODE="+(bytes[3]&15));if(bytes.Length<question.Length||!bytes.Skip(12).Take(question.Length-12).SequenceEqual(question.Skip(12)))throw new FormatException("Другое DNS-имя в ответе");int pos=question.Length;var result=new List<string>();int answers=U16(bytes,6);if(answers>512)throw new FormatException();for(int i=0;i<answers;i++){SkipName(bytes,ref pos);int type=U16(bytes,pos),cls=U16(bytes,pos+2),length=U16(bytes,pos+8);pos+=10;if(pos+length>bytes.Length)throw new FormatException();if(cls==1&&((type==1&&length==4)||(type==28&&length==16)))result.Add(new IPAddress(bytes.Skip(pos).Take(length).ToArray()).ToString());pos+=length;}if(result.Count==0)throw new FormatException("Нет A/AAAA ответа");return result.Distinct().ToArray();}
  public static async Task<DnsMeasurement> Query(string server,string host,CancellationToken ct){IPAddress address;if(!IPAddress.TryParse(server,out address))throw new ArgumentException("DNS-сервер должен быть IP-адресом");host=NetworkInput.Host(host);var result=new DnsMeasurement {Server=address.ToString(),Host=host};byte[] random=new byte[2];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(random);ushort id=(ushort)(random[0]*256+random[1]);byte[] question=Question(host,id);var watch=Stopwatch.StartNew();using(var udp=new UdpClient(address.AddressFamily)){udp.Connect(address,53);try {ct.ThrowIfCancellationRequested();await udp.SendAsync(question,question.Length);var reply=await NetworkInput.Bounded(udp.ReceiveAsync(),1500,ct);ct.ThrowIfCancellationRequested();result.Answers=Answer(reply.Buffer,id,question);result.Success=true;}catch(OperationCanceledException){throw;}catch(Exception e){result.Error=e is TimeoutException?"DNS timeout":e is SocketException?"DNS network error":e.Message;}result.Milliseconds=watch.Elapsed.TotalMilliseconds;}return result;}
 }
}
