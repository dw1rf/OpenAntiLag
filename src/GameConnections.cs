using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
namespace OpenAntiLag {
 public sealed class GameProcess {
  public int Id;public string Name,Path;public bool KnownGame;public override string ToString(){return Name+" · PID "+Id+(KnownGame?" · игра":"");}
 }
 public sealed class ProcessConnection {
  public int ProcessId;public string ProcessName,Protocol,LocalAddress,RemoteAddress,State;public int LocalPort,RemotePort;
 }
 public static class GameConnections {
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int process);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder path,ref int size);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  public static bool ProtectedName(string name){name=(name??"").ToLowerInvariant();return new[]{"faceit","anticheat","anti-cheat","battleye","easyanti","vgc","vgtray","vanguard","msmpeng","securityhealth","openantilag","system","svchost","services","lsass","wininit","csrss","smss","winlogon"}.Any(x=>name.Contains(x));}
  static string ImagePath(int pid){IntPtr handle=OpenProcess(0x1000,false,pid);if(handle==IntPtr.Zero)return null;try{var path=new StringBuilder(32768);int count=path.Capacity;return QueryFullProcessImageName(handle,0,path,ref count)?path.ToString():null;}finally{CloseHandle(handle);}}

  [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table,ref int size,bool order,int family,int kind,uint reserved);
  [DllImport("iphlpapi.dll")] static extern uint GetExtendedUdpTable(IntPtr table,ref int size,bool order,int family,int kind,uint reserved);
  public static readonly string[] Known={"cs2","dota2","valorant-win64-shipping","fortniteclient-win64-shipping","r5apex","hl2","overwatch","bg3","bg3_dx11","cyberpunk2077","valheim","palworld-win64-shipping"};
  public static GameProcess[] Processes(){var list=new List<GameProcess>();foreach(var process in Process.GetProcesses())using(process){try {if(process.Id<=4)continue;string name=process.ProcessName;var item=new GameProcess {Id=process.Id,Name=name,KnownGame=Known.Contains(name.ToLowerInvariant())};if(!ProtectedName(name))item.Path=ImagePath(process.Id);list.Add(item);}catch(InvalidOperationException){}}return list.OrderByDescending(p=>p.KnownGame).ThenBy(p=>p.Name).ToArray();}
  public static List<ProcessConnection> Read(){var result=new List<ProcessConnection>();foreach(bool tcp in new[]{true,false})foreach(int family in new[]{2,23}) {int size=0;uint code=tcp?GetExtendedTcpTable(IntPtr.Zero,ref size,false,family,5,0):GetExtendedUdpTable(IntPtr.Zero,ref size,false,family,1,0);if(code!=0&&code!=122)throw new Win32Exception((int)code);for(int attempt=0;attempt<3;attempt++){if(size<4||size>16*1024*1024)throw new IOException("Неверный размер таблицы соединений");IntPtr buffer=Marshal.AllocHGlobal(size);try{int capacity=size;code=tcp?GetExtendedTcpTable(buffer,ref size,false,family,5,0):GetExtendedUdpTable(buffer,ref size,false,family,1,0);if(code==122)continue;if(code!=0)throw new Win32Exception((int)code);var bytes=new byte[capacity];Marshal.Copy(buffer,bytes,0,capacity);result.AddRange(Parse(bytes,tcp,family));break;}finally{Marshal.FreeHGlobal(buffer);}}if(code!=0)throw new Win32Exception((int)code);}return result;}
  static uint U32(byte[] b,int i){return BitConverter.ToUInt32(b,i);}static int Port(byte[] b,int i){return b[i]*256+b[i+1];}
  public static List<ProcessConnection> Parse(byte[] b,bool tcp,int family){if(b.Length<4)throw new InvalidDataException();bool v6=family==23;int row=tcp?(v6?56:24):(v6?28:12),count=checked((int)U32(b,0));if(count<0||count>(b.Length-4)/row)throw new InvalidDataException("Повреждена таблица соединений");var result=new List<ProcessConnection>();for(int n=0;n<count;n++){int i=4+n*row;var item=new ProcessConnection {Protocol=tcp?"TCP":"UDP",State=tcp?((System.Net.NetworkInformation.TcpState)U32(b,i+(v6?48:0))).ToString():"Локальная привязка; удалённый адрес Windows не сообщает"};int offset=tcp&&!v6?4:0;item.LocalAddress=v6?new IPAddress(b.Skip(i).Take(16).ToArray(),U32(b,i+16)).ToString():new IPAddress(b.Skip(i+offset).Take(4).ToArray()).ToString();item.LocalPort=Port(b,i+(v6?20:tcp?8:4));item.ProcessId=(int)U32(b,i+(tcp?(v6?52:20):(v6?24:8)));if(tcp){item.RemoteAddress=v6?new IPAddress(b.Skip(i+24).Take(16).ToArray(),U32(b,i+40)).ToString():new IPAddress(b.Skip(i+12).Take(4).ToArray()).ToString();item.RemotePort=Port(b,i+(v6?44:16));}result.Add(item);}return result;}
  public static bool IsProtected(string path){if(String.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathRooted(path)||System.IO.Path.GetExtension(path).ToLowerInvariant()!=".exe")return true;path=System.IO.Path.GetFullPath(path);string name=System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\')+"\\";return path.StartsWith(windows,StringComparison.OrdinalIgnoreCase)||ProtectedName(name)||new[]{"faceit","anticheat","anti-cheat","easyanti","battleye","vanguard","windows defender"}.Any(part=>path.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0);}
 }
}
