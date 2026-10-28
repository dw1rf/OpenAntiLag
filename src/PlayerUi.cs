using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
namespace OpenAntiLag {
 public sealed class PlayerActionState {public bool Running;}
 public static class PlayerUi {
  public static Label Text(string text,float size=10,bool bold=false,Color? color=null){return new Label {Text=text,AutoSize=true,ForeColor=color??Theme.Text,BackColor=Color.Transparent,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),Margin=new Padding(0,0,0,10),MaximumSize=new Size(1000,0),UseMnemonic=false};}
  public static Image GameIcon(InstalledGame game){var image=GameDetectionService.Artwork(game);if(image!=null||game==null)return image;var bitmap=new Bitmap(64,64);using(var g=Graphics.FromImage(bitmap)){g.Clear(Theme.Surface);Theme.Fill(g,new Rectangle(0,0,63,63),Theme.Selected,Theme.Accent,12);using(var font=new Font("Segoe UI",18,FontStyle.Bold))TextRenderer.DrawText(g,game.Name.StartsWith("Counter-Strike",StringComparison.OrdinalIgnoreCase)?"CS":new string(game.Name.Where(Char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant(),font,new Rectangle(0,0,64,64),Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}return bitmap;}
  public static ProfileButton Button(string text,string name=null,bool primary=false){return new ProfileButton {Text=text,Name=name??text,Primary=primary,Quiet=!primary,Width=220,Height=44,Margin=new Padding(0,6,10,4),AccessibleName=text};}
  public static ComboBox Choice(params string[] items){var combo=new ThemedComboBox {Width=280,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,4,0,10)};combo.Items.AddRange(items);if(combo.Items.Count>0)combo.SelectedIndex=0;return combo;}
  public static void Action(Button button,Func<Task> action,PlayerContext context){var state=new PlayerActionState();button.Tag=state;button.Click+=async delegate{if(state.Running||context.Optimizer.Status.Busy){context.Notice="Дождитесь завершения текущего изменения настроек.";context.Emit();return;}state.Running=true;button.Enabled=false;try{await action();}catch(Exception error){context.Notice=error is OperationCanceledException?"Действие отменено. Настройки не применялись.":"Не удалось выполнить «"+button.Text+"». Причина сохранена в разделе «Результаты».";context.Optimizer.ReportExternal(button.Text,error.Message,false);context.Emit();}finally{state.Running=false;if(!button.IsDisposed)button.Enabled=!context.Optimizer.Status.Busy;context.Emit();}};}
  public static bool Confirm(Control owner,string title,string summary,bool preview){return preview||MessageBox.Show(owner,summary,title,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
 }
 public class PlayerCard:TableLayoutPanel {
  public PlayerCard(){AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;ColumnCount=1;Padding=new Padding(20);Margin=new Padding(0,0,16,16);BackColor=Theme.Surface;SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));}
  public void Add(Control control){Controls.Add(control);RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;}
  protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);foreach(var label in Controls.OfType<Label>()){var size=new Size(Math.Max(100,ClientSize.Width-Padding.Horizontal-4),0);if(label.MaximumSize!=size)label.MaximumSize=size;}}
  protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Theme.Background);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;Theme.Fill(e.Graphics,new Rectangle(0,0,Width-1,Height-1),Theme.Surface,Theme.Border,14);}
 }
 public sealed class CardGrid:FlowLayoutPanel {
  public CardGrid(){AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Dock=DockStyle.Top;WrapContents=true;FlowDirection=FlowDirection.LeftToRight;Margin=Padding.Empty;}
  protected override void OnLayout(LayoutEventArgs levent){int available=ClientSize.Width;int columns=available>=900*Math.Max(Font.Size/10f,DeviceDpi/96f)?2:1;int width=Math.Max(180,(available-20)/columns-8);foreach(Control child in Controls){var minimum=new Size(width,0);if(child.MinimumSize!=minimum)child.MinimumSize=minimum;if(child.MaximumSize!=minimum)child.MaximumSize=minimum;if(child.Width!=width)child.Width=width;}base.OnLayout(levent);}
 }
 public class PlayerPage:Panel {
  public readonly TableLayoutPanel Body=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=1,Padding=new Padding(28,22,24,24),Margin=Padding.Empty};
  protected readonly PlayerContext Context;protected readonly Label Notice;readonly int uiThread=Thread.CurrentThread.ManagedThreadId;readonly SynchronizationContext uiContext=SynchronizationContext.Current??new WindowsFormsSynchronizationContext();
  public PlayerPage(PlayerContext context,string title,string subtitle){Context=context;Dock=DockStyle.Fill;AutoScroll=true;BackColor=Theme.Background;Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(Body);Add(PlayerUi.Text(title,24,true));Add(PlayerUi.Text(subtitle,10,false,Theme.Muted));Notice=PlayerUi.Text("",10,false,Theme.Muted);Notice.Name="playerNotice";Add(Notice);HandleCreated+=delegate{WindowTheme.Scrollbar(Handle);};context.Changed+=Changed;}
  public void Add(Control control){Body.Controls.Add(control);Body.RowStyles.Add(new RowStyle(SizeType.AutoSize));control.Dock=DockStyle.Top;}
  void Changed(){if(IsDisposed)return;if(Thread.CurrentThread.ManagedThreadId!=uiThread){try{uiContext.Post(_=>Changed(),null);}catch(InvalidOperationException){}return;}Notice.Text=Context.Notice??"";Notice.Visible=!String.IsNullOrEmpty(Notice.Text);RefreshState();foreach(var button in Descendants(this).OfType<Button>()){var action=button.Tag as PlayerActionState;if(action!=null)button.Enabled=Context.Initialized&&!Context.Optimizer.Status.Busy&&!action.Running&&(!Context.Preview||button.Name!="restoreAllModules"&&!button.Name.StartsWith("restore_"));}}
  static IEnumerable<Control> Descendants(Control root){foreach(Control child in root.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
  public virtual void RefreshState(){}
  protected override void Dispose(bool disposing){if(disposing)Context.Changed-=Changed;base.Dispose(disposing);}
 }
 public sealed class NavigationController:UserControl {
  readonly Panel content=new Panel {Dock=DockStyle.Fill};readonly FlowLayoutPanel menu=new FlowLayoutPanel {Dock=DockStyle.Left,Width=186,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(16,28,12,16),BackColor=Theme.Surface};readonly Dictionary<string,Control> pages=new Dictionary<string,Control>();readonly Dictionary<string,Button> buttons=new Dictionary<string,Button>();
  public string Current {get;private set;}public event Action<string> Navigated;
  public NavigationController(){Dock=DockStyle.Fill;BackColor=Theme.Background;Controls.Add(content);Controls.Add(menu);var brand=PlayerUi.Text("OPEN\nANTILAG",18,true);brand.Margin=new Padding(6,0,0,30);menu.Controls.Add(brand);}
  public void Register(string key,string caption,Control page){pages[key]=page;var button=PlayerUi.Button(caption,"nav"+key);button.Width=156;button.Height=46;button.TextAlign=ContentAlignment.MiddleLeft;button.Margin=new Padding(0,0,0,8);button.Click+=delegate{ShowPage(key);};buttons[key]=button;menu.Controls.Add(button);}
  public void AddVersion(){var label=PlayerUi.Text("v"+Updates.Current.ToString(3)+"  ·  локальные профили",8.5f,false,Theme.Muted);label.Margin=new Padding(2,22,0,0);label.MaximumSize=new Size(156,0);menu.Controls.Add(label);}
  public void RegisterDetail(string key,Control page){pages[key]=page;}
  public void ShowPage(string key){Control page;if(!pages.TryGetValue(key,out page))return;Current=key;content.SuspendLayout();content.Controls.Clear();page.Dock=DockStyle.Fill;content.Controls.Add(page);page.Show();content.ResumeLayout(true);foreach(var p in buttons){((ProfileButton)p.Value).Primary=p.Key==key;p.Value.Invalidate();}var player=page as PlayerPage;if(player!=null)player.RefreshState();var handler=Navigated;if(handler!=null)handler(key);}
  public void ShowDetail(Control view){content.Controls.Clear();view.Dock=DockStyle.Fill;content.Controls.Add(view);view.Show();}
  protected override void Dispose(bool disposing){if(disposing)foreach(var page in pages.Values)page.Dispose();base.Dispose(disposing);}
 }
 public sealed class ExpertHost:Panel {
  readonly TabControl tabs;readonly Panel mask=new Panel {Dock=DockStyle.Fill};readonly Label title=PlayerUi.Text("Дополнительные настройки",16,true);readonly Button back=PlayerUi.Button("← Назад",null,false);
  public ExpertHost(TabControl tabs,Action returnToSettings){this.tabs=tabs;Dock=DockStyle.Fill;Padding=new Padding(12);var top=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8)};top.Controls.Add(back);top.Controls.Add(title);back.Click+=delegate{returnToSettings();};Controls.Add(mask);Controls.Add(top);mask.Controls.Add(tabs);tabs.Dock=DockStyle.None;mask.Resize+=delegate{LayoutTabs();};tabs.SelectedIndexChanged+=delegate{title.Text="Дополнительно · "+tabs.SelectedTab.Text;LayoutTabs();};}
  void LayoutTabs(){if(tabs.TabCount==0||mask.Width<10||mask.Height<10)return;tabs.SetBounds(-4,-64,mask.Width+8,mask.Height+68);int header=0;for(int i=0;i<tabs.TabCount;i++)header=Math.Max(header,tabs.GetTabRect(i).Bottom);tabs.SetBounds(-4,-header-4,mask.Width+8,mask.Height+header+8);}
  public void Select(int index,string text){title.Text=text;tabs.SelectedIndex=index;LayoutTabs();}
 }
}
