using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class ShaderPage : TabPage {
        readonly ShaderEngine engine;
        readonly Func<bool> acquire;readonly Action release;readonly bool preview;
        readonly ListView games=new ListView {Name="shaderGames",View=View.Details,FullRowSelect=true,MultiSelect=true,HideSelection=false,Dock=DockStyle.Fill,BackColor=Theme.Surface,ForeColor=Theme.Text,BorderStyle=BorderStyle.None};
        readonly TextBox output=new TextBox {Name="shaderLog",Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Theme.Background,ForeColor=Theme.Text,BorderStyle=BorderStyle.None};
        readonly Label status=new Label {Name="shaderStatus",AutoSize=true,Dock=DockStyle.Fill,ForeColor=Theme.Text};
        readonly ProfileButton install=new ProfileButton {Name="installShaders",Text="Загрузить модуль"},scan=new ProfileButton {Name="scanShaders",Text="Найти игры"},compile=new ProfileButton {Name="compileShaders",Text="Подготовить",Primary=true},details=new ProfileButton {Text="Состояние игры"},cache=new ProfileButton {Text="Размер кэша"},stop=new ProfileButton {Name="stopShaders",Text="Остановить"};
        readonly ProgressBar progress=new ProgressBar {Dock=DockStyle.Fill,Height=5,Style=ProgressBarStyle.Marquee,Visible=false};
        readonly Func<string,string,Action<string>,CancellationToken,Task<string>> run;
        CancellationTokenSource cancellation;bool running;
        public ShaderPage(bool preview,Func<bool> acquire,Action release,ShaderEngine engine=null,Func<string,string,Action<string>,CancellationToken,Task<string>> run=null):base("Шейдеры") {
            this.preview=preview;this.acquire=acquire;this.release=release;this.engine=engine??new ShaderEngine();this.run=run??this.engine.Run;
            BackColor=Theme.Background;ForeColor=Theme.Text;Padding=new Padding(18);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,55));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,45));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var heading=new Label {Text="Подготовка шейдеров перед игрой",AutoSize=true,Dock=DockStyle.Fill,Font=new Font("Segoe UI",16,FontStyle.Bold),ForeColor=Theme.Text};
            var note=new Label {Text="Движок SCSKiller · NVIDIA / AMD. Закройте игру перед подготовкой.\nДо 4 потоков, низкий приоритет. Работает только по вашей кнопке.\nРекордер, установка DLL в игры и очистка кэша здесь отключены.",AutoSize=true,Dock=DockStyle.Fill,ForeColor=Theme.Muted,Margin=new Padding(0,8,0,12)};
            root.Controls.Add(heading,0,0);root.Controls.Add(note,0,1);
            var setup=Buttons(install,scan,cache);root.Controls.Add(setup,0,2);
            games.Columns.Add("Игра",250);games.Columns.Add("Состояние",180);games.Columns.Add("Причина / сведения",400);games.Margin=new Padding(0,12,0,12);root.Controls.Add(games,0,3);
            games.OwnerDraw=true;
            games.DrawColumnHeader+=delegate(object sender,DrawListViewColumnHeaderEventArgs e){using(var brush=new SolidBrush(Theme.Surface))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,e.Header.Text,games.Font,Rectangle.Inflate(e.Bounds,-6,0),Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
            games.DrawSubItem+=delegate(object sender,DrawListViewSubItemEventArgs e){using(var brush=new SolidBrush(e.Item.Selected?Theme.Selected:Theme.Surface))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,e.SubItem.Text,games.Font,Rectangle.Inflate(e.Bounds,-6,0),Theme.Text,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);};
            games.HandleCreated+=delegate{WindowTheme.Scrollbar(games.Handle);};output.HandleCreated+=delegate{WindowTheme.Scrollbar(output.Handle);};
            root.Controls.Add(Buttons(compile,details,stop),0,4);root.Controls.Add(progress,0,5);output.Margin=new Padding(0,12,0,8);root.Controls.Add(output,0,6);root.Controls.Add(status,0,7);Controls.Add(root);
            install.Click+=async delegate {await Work(async ct=>{await this.engine.Install(Log,ct);},"Загрузка официального модуля…");};
            scan.Click+=async delegate {await Work(async ct=>{var text=await this.run("scan",null,Log,ct);ShowGames(ShaderProtocol.Games(text));},"Поиск установленных игр…");};
            cache.Click+=async delegate {await Work(async ct=>{await this.run("cache",null,Log,ct);},"Чтение размера кэша…");};
            details.Click+=async delegate {var selected=Selected();if(selected.Count!=1)return;await Work(async ct=>{await this.run("status",selected[0].Id,Log,ct);},"Чтение состояния игры…");};
            compile.Click+=async delegate {
                var selected=Selected();if(selected.Count==0||selected.Any(x=>!x.CanCompile))return;
                await Work(async ct=>{foreach(var game in selected){ct.ThrowIfCancellationRequested();Log("Подготовка: "+game.Name);await this.run("queue",game.Id,Log,ct);}var text=await this.run("scan",null,Log,ct);ShowGames(ShaderProtocol.Games(text));},"Подготовка шейдеров. Можно остановить; завершение текущих операций займёт время…");
            };
            stop.Click+=delegate {if(cancellation!=null){stop.Enabled=false;status.Text="Остановка: ждём завершения текущих операций и записи кэша…";cancellation.Cancel();}};
            games.SelectedIndexChanged+=delegate {UpdateButtons();};
            if(preview){ShowGames(new List<ShaderGame>{new ShaderGame {Id="steam:example",Name="Пример игры — предпросмотр",Status="Ready",Reason="Здесь появятся найденные игры и состояние шейдеров."}});}
            status.Text=this.engine.Installed?"Модуль установлен. Нажмите «Найти игры».":"Модуль SCSKiller "+ShaderEngine.Version+": загрузка около 120 МБ с проверкой SHA-256. Интернет нужен для загрузки.";
            UpdateButtons();
        }
        static TableLayoutPanel Buttons(params Button[] buttons) {
            var row=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,ColumnCount=buttons.Length,Margin=Padding.Empty};
            foreach(var button in buttons){row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/buttons.Length));button.Dock=DockStyle.Fill;button.Height=48;button.Margin=new Padding(3);row.Controls.Add(button);}return row;
        }
        List<ShaderGame> Selected(){return games.SelectedItems.Cast<ListViewItem>().Select(x=>(ShaderGame)x.Tag).ToList();}
        void ShowGames(List<ShaderGame> list) {
            games.BeginUpdate();games.Items.Clear();foreach(var game in list){var item=new ListViewItem(new[]{game.Name,game.StatusText,game.ReasonText}){Tag=game};games.Items.Add(item);}games.EndUpdate();
            Log("Найдено игр: "+list.Count+". «Нужна запись игры» означает, что подготовка без рекордера недоступна.");UpdateButtons();
        }
        void UpdateButtons() {
            bool ready=!preview&&!running&&(engine.Installed||run.Target!=engine);var selected=Selected();
            install.Enabled=!preview&&!running&&!engine.Installed;scan.Enabled=cache.Enabled=ready;details.Enabled=ready&&selected.Count==1;compile.Enabled=ready&&selected.Count>0&&selected.All(x=>x.CanCompile);stop.Enabled=running;games.Enabled=!running;
        }
        void Log(string line) {
            if(IsDisposed||Disposing||!IsHandleCreated)return;
            if(InvokeRequired){try{BeginInvoke((Action<string>)Log,line);}catch(InvalidOperationException){}return;}
            if(output.TextLength>150000)output.Text=output.Text.Substring(output.TextLength-100000);
            output.AppendText(line+Environment.NewLine);
        }
        async Task Work(Func<CancellationToken,Task> action,string message) {
            if(preview||running)return;if(!acquire()){status.Text="Дождитесь завершения другой операции Open AntiLag.";return;}
            running=true;cancellation=new CancellationTokenSource();progress.Visible=true;status.Text=message;UpdateButtons();
            try {await action(cancellation.Token);if(!IsDisposed)status.Text="Операция завершена. Результат показан в списке и журнале.";}
            catch(OperationCanceledException){if(!IsDisposed)status.Text="Остановлено. Повторная подготовка доступна по кнопке.";}
            catch(Exception error){Log(error.Message);if(!IsDisposed)status.Text="Не завершено: "+error.Message;Program.Log("Shaders: "+error.Message);}
            finally {cancellation.Dispose();cancellation=null;running=false;release();if(!IsDisposed){progress.Visible=false;UpdateButtons();}}
        }
        protected override void Dispose(bool disposing){if(disposing&&cancellation!=null)cancellation.Cancel();base.Dispose(disposing);}
    }
}
