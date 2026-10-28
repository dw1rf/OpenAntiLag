using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;
namespace OpenAntiLag {
 public enum PlayerProfile {Standard,Competitive,Custom}
 public sealed class InstalledGame {
  public string Id,Name,InstallDirectory,Executable,Artwork;public PlayerProfile Profile=PlayerProfile.Standard;public bool Automatic;public string LastResult;
  public override string ToString(){return Name;}
  public bool CanWatch {get{return !String.IsNullOrEmpty(Executable)&&File.Exists(Executable);}}
 }
 public sealed class PlayerPreferences {public int Version=1;public bool SetupComplete,LocalMonitoring;public PlayerProfile DefaultProfile=PlayerProfile.Standard;public string SelectedGame;public List<InstalledGame> Games=new List<InstalledGame>();}
 public interface IPlayerStore {PlayerPreferences Load();void Save(PlayerPreferences value);}
 public sealed class PlayerStore:IPlayerStore {
  readonly string path;public PlayerStore(string path=null){this.path=path??Path.Combine(Program.DataDirectory,"player-library.json");}
  public static void Validate(PlayerPreferences value){if(value==null||value.Version!=1||!Enum.IsDefined(typeof(PlayerProfile),value.DefaultProfile)||value.Games==null||value.Games.Count>5000||value.Games.Any(g=>g==null||String.IsNullOrEmpty(g.Id)||String.IsNullOrWhiteSpace(g.Name)||g.Name.Length>256||!Enum.IsDefined(typeof(PlayerProfile),g.Profile)||(g.Executable!=null&&!Path.IsPathRooted(g.Executable))||(g.Automatic&&(g.Profile==PlayerProfile.Custom||String.IsNullOrEmpty(g.Executable))))||value.Games.Select(g=>g.Id).Distinct().Count()!=value.Games.Count)throw new InvalidDataException("Библиотека игр повреждена. Автоматическое применение отключено.");}
  public PlayerPreferences Load(){if(!File.Exists(path))return new PlayerPreferences();if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("Библиотека игр слишком велика");string text=File.ReadAllText(path);var serializer=new JavaScriptSerializer {MaxJsonLength=4*1024*1024};var schema=serializer.DeserializeObject(text) as Dictionary<string,object>;if(schema==null||!schema.ContainsKey("Version")||!schema.ContainsKey("Games"))throw new InvalidDataException("Библиотека игр не содержит обязательных полей");var value=serializer.Deserialize<PlayerPreferences>(text);Validate(value);return value;}
  public void Save(PlayerPreferences value){Validate(value);AtomicLocalFile.Write(path,Encoding.UTF8.GetBytes(new JavaScriptSerializer {MaxJsonLength=4*1024*1024}.Serialize(value)));}
 }
 public sealed class GameDetectionService {
  readonly Func<string[]> roots;public GameDetectionService(Func<string[]> roots=null){this.roots=roots??SteamRoots;}
  static string[] SteamRoots(){var result=new List<string>();foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})foreach(var path in new[]{@"Software\Valve\Steam",@"Software\WOW6432Node\Valve\Steam"})using(var key=hive.OpenSubKey(path)){if(key==null)continue;string root=(key.GetValue("SteamPath")??key.GetValue("InstallPath")) as string;if(!String.IsNullOrWhiteSpace(root))result.Add(root);}return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
  public static string ReadVdf(string text,string name){var m=Regex.Match(text,"\""+Regex.Escape(name)+"\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"",RegexOptions.IgnoreCase);return m.Success?Unescape(m.Groups[1].Value):null;}
  static string Unescape(string text){return text.Replace("\\\\","\\").Replace("\\\"","\"");}
  static string ReadSmall(string path){if(!File.Exists(path)||new FileInfo(path).Length>2*1024*1024)return "";return File.ReadAllText(path);}
  public List<InstalledGame> Scan(){var result=new List<InstalledGame>();var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string root in roots()){if(!Directory.Exists(root))continue;libraries.Add(Path.GetFullPath(root));string folders=ReadSmall(Path.Combine(root,"steamapps","libraryfolders.vdf"));foreach(Match m in Regex.Matches(folders,"\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"",RegexOptions.IgnoreCase)){string value=Unescape(m.Groups[1].Value);if(Path.IsPathRooted(value)&&Directory.Exists(value))libraries.Add(Path.GetFullPath(value));}}
   foreach(string library in libraries){string steamapps=Path.Combine(library,"steamapps");if(!Directory.Exists(steamapps))continue;foreach(string manifest in Directory.GetFiles(steamapps,"appmanifest_*.acf").Take(5000)){try{var text=ReadSmall(manifest);string id=ReadVdf(text,"appid"),name=ReadVdf(text,"name"),folder=ReadVdf(text,"installdir");uint number;if(!UInt32.TryParse(id,out number)||String.IsNullOrWhiteSpace(name)||String.IsNullOrWhiteSpace(folder)||folder.IndexOfAny(Path.GetInvalidFileNameChars())>=0||folder=="..")continue;string install=Path.GetFullPath(Path.Combine(steamapps,"common",folder));if(!Directory.Exists(install))continue;string executable=FindExe(install,id);string art=roots().SelectMany(r=>new[]{Path.Combine(r,"appcache","librarycache",id+"_header.jpg"),Path.Combine(r,"appcache","librarycache",id+"_icon.jpg"),Path.Combine(r,"appcache","librarycache",id,"library_600x900.jpg")}).FirstOrDefault(File.Exists);result.Add(new InstalledGame {Id="steam:"+id,Name=name,InstallDirectory=install,Executable=executable,Artwork=art});}catch(IOException){}catch(UnauthorizedAccessException){}}}
   return result.GroupBy(g=>g.Id).Select(g=>g.First()).OrderBy(g=>g.Name).ToList();
  }
  static string FindExe(string install,string app){if(app=="730"){string cs2=Path.Combine(install,"game","bin","win64","cs2.exe");return File.Exists(cs2)?cs2:null;}foreach(string sub in new[]{"","bin","bin\\win64","Binaries\\Win64","game\\bin\\win64"}){string directory=Path.Combine(install,sub);if(!Directory.Exists(directory))continue;var matches=Directory.GetFiles(directory,"*.exe").Where(p=>!new[]{"unins","setup","crash","redist","benchmark","launcher"}.Any(x=>Path.GetFileName(p).ToLowerInvariant().Contains(x))).ToArray();if(matches.Length==1)return matches[0];}return null;}
  public static InstalledGame Manual(string executable){executable=Path.GetFullPath(executable);if(!File.Exists(executable)||!Path.GetExtension(executable).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Выберите существующий EXE игры");string id;using(var sha=SHA256.Create())id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(executable.ToLowerInvariant()))).Replace("-","");return new InstalledGame {Id="manual:"+id,Name=Path.GetFileNameWithoutExtension(executable),Executable=executable,InstallDirectory=Path.GetDirectoryName(executable)};}
  public static Image Artwork(InstalledGame game){if(game==null)return null;try{if(!String.IsNullOrEmpty(game.Artwork)&&File.Exists(game.Artwork)&&new FileInfo(game.Artwork).Length<8*1024*1024)using(var image=Image.FromFile(game.Artwork))return image.Width<=4096&&image.Height<=4096?new Bitmap(image):null;if(game.CanWatch)using(var icon=Icon.ExtractAssociatedIcon(game.Executable))return icon==null?null:icon.ToBitmap();}catch(OutOfMemoryException){}catch(ArgumentException){}catch(IOException){}catch(System.ComponentModel.Win32Exception){}return null;}
 }
}
