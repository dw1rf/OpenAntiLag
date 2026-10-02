using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class MainForm : Form {
        readonly Engine engine;
        readonly bool preview, startHidden;
        readonly Label status=new Label(), detail=new Label();
        readonly OptionCard gameMode=new OptionCard(), capture=new OptionCard(), mouse=new OptionCard(), cpu=new OptionCard(), pcie=new OptionCard(), timer=new OptionCard(), startup=new OptionCard { Compact=true };
        readonly ProfileButton enable=new ProfileButton { Primary=true }, disable=new ProfileButton();
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer poll;
        readonly Panel scroll=new Panel { Dock=DockStyle.Fill, AutoScroll=true };
        readonly TableLayoutPanel content=new TableLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Dock=DockStyle.Top, ColumnCount=1, Margin=Padding.Empty };
        bool busy, exiting, initializing=true;
        public MainForm(Engine engine,bool startHidden,bool preview) {
            this.engine=engine; this.preview=preview; this.startHidden=startHidden;
            Text="Open AntiLag"; Font=new Font("Segoe UI",10); BackColor=Theme.Background; ForeColor=Theme.Text;
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(800,850); MinimumSize=new Size(640,570); StartPosition=FormStartPosition.CenterScreen; Icon=SystemIcons.Application;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), ColumnCount=1, RowCount=5, BackColor=Theme.Background };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            var header=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=2, Margin=new Padding(0,0,0,20) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Label("Open AntiLag",24,Theme.Text,true),0,0);
            var version=Label("v0.3",10,Theme.Muted,false); version.Anchor=AnchorStyles.Right; header.Controls.Add(version,1,0);
            var subtitle=Label("Настройки для игры. Один профиль, полный откат.",10,Theme.Muted,false); header.Controls.Add(subtitle,0,1); header.SetColumnSpan(subtitle,2); root.Controls.Add(header,0,0);
            var stateCard=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=1, BackColor=Theme.Selected, Padding=new Padding(18,14,18,14), Margin=new Padding(0,0,0,20) };
            stateCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            status.AutoSize=true; status.Dock=DockStyle.Fill; status.Text="Проверка состояния…"; status.Font=new Font(Font.FontFamily,16,FontStyle.Bold); status.ForeColor=Theme.Text; status.Margin=new Padding(0,0,0,6);
            detail.AutoSize=true; detail.Dock=DockStyle.Fill; detail.Text="Читаем сохранённый профиль."; detail.ForeColor=Theme.Muted; detail.Margin=Padding.Empty;
            stateCard.Controls.Add(status); stateCard.Controls.Add(detail); root.Controls.Add(stateCard,0,1);
            root.Controls.Add(scroll,0,2); scroll.Controls.Add(content); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            scroll.HandleCreated+=delegate { WindowTheme.Scrollbar(scroll.Handle); };
            var plan=Label("Профиль производительности",12,Theme.Text,true); plan.Margin=new Padding(0,0,0,6); content.Controls.Add(plan);
            var powerNote=Label("Отдельный план питания. Исходные настройки сохраняются до включения.",10,Theme.Muted,false); powerNote.Margin=new Padding(0,0,0,16); content.Controls.Add(powerNote);
            var grid=new TableLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Dock=DockStyle.Top, ColumnCount=2, Margin=Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var options=engine.State.Phase=="Disabled" ? ProfileOptions.Gaming() : engine.State.Options;
            AddOption(grid,gameMode,"Игровой режим Windows","Включить Game Mode для игр.",0,0,options.GameMode);
            AddOption(grid,capture,"Отключить запись Xbox","Без фоновой записи и игровых клипов.",1,0,options.DisableCapture);
            AddOption(grid,mouse,"Мышь без ускорения","Равномерное движение курсора. Raw Input не меняется.",0,1,options.DisableMouseAcceleration);
            AddOption(grid,cpu,"CPU без парковки","От сети: минимум CPU 100%. Больше нагрев и расход.",1,1,options.CpuReady);
            AddOption(grid,pcie,"Питание PCIe","Отключить энергосбережение PCIe при питании от сети.",0,2,options.PcieReady);
            AddOption(grid,timer,"Таймер 1 мс","Эксперимент. Эффект в играх не гарантирован.",1,2,engine.State.TimerRequested); content.Controls.Add(grid);
            var note=Label("Профиль может увеличить нагрев и расход энергии. После применения перезапустите игру.",9.5f,Theme.Muted,false); note.Margin=new Padding(0,2,0,12); content.Controls.Add(note);
            var actions=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=2, Margin=new Padding(0,16,0,12) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            StyleButton(enable,"Включить профиль"); StyleButton(disable,"Отключить"); enable.Name="enable"; disable.Name="disable"; enable.Margin=new Padding(0,0,6,0); disable.Margin=new Padding(6,0,0,0);
            enable.Enabled=disable.Enabled=false; actions.Controls.Add(enable); actions.Controls.Add(disable); root.Controls.Add(actions,0,3);
            enable.Click+=async delegate { await ChangeProfile(true); }; disable.Click+=async delegate { await ChangeProfile(false); };
            var footer=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=3, Margin=Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            startup.Text="Запускать с Windows"; startup.Dock=DockStyle.Fill; startup.Checked=!preview && Startup.Enabled; startup.Margin=Padding.Empty; footer.Controls.Add(startup,0,0);
            startup.CheckedChanged+=delegate { if(initializing||preview)return; try { Startup.Enabled=startup.Checked; } catch(Exception error) { initializing=true; startup.Checked=!startup.Checked; initializing=false; Report(error); } };
            footer.Controls.Add(Link("Журнал",delegate { if(!preview) { Directory.CreateDirectory(Program.DataDirectory); Process.Start("explorer.exe","\""+Program.DataDirectory+"\""); } }),1,0);
            footer.Controls.Add(Link("О программе",delegate { MessageBox.Show(this,"Open AntiLag 0.3.0 • MIT\n\nИсходные значения сохраняются до изменений. Внешние изменения пользователя при откате сохраняются.\n\nПрофиль не гарантирует прирост FPS или нулевую задержку. Настройки Xbox зависят от версии Windows. Таймер 1 мс не является универсальной оптимизацией игр.\n\nBCD, HPET, HAGS и защита Windows не изменяются.","Open AntiLag",MessageBoxButtons.OK,MessageBoxIcon.Information); }),2,0); root.Controls.Add(footer,0,4);
            var menu=new ContextMenuStrip { BackColor=Theme.Surface, ForeColor=Theme.Text };
            menu.Items.Add("Открыть",null,delegate { Reveal(); });
            menu.Items.Add("Выход",null,delegate { if(busy)return; if(engine.State.Phase!="Disabled" && MessageBox.Show(this,"Настройки профиля останутся применены. Для отката сначала нажмите «Отключить».\n\nВыйти?","Open AntiLag",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return; exiting=true; Close(); });
            tray=new NotifyIcon { Icon=Icon,Text="Open AntiLag",ContextMenuStrip=menu,Visible=!preview }; tray.DoubleClick+=delegate { Reveal(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(!exiting && !preview && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
            Resize+=delegate { if(WindowState==FormWindowState.Minimized&&!preview)Hide(); };
            poll=new System.Windows.Forms.Timer { Interval=5000 }; poll.Tick+=async delegate { if(!busy&&Visible)await RefreshState(); };
            Shown+=async delegate { if(startHidden)Hide(); busy=true; try { await Task.Run((Action)engine.Resume); } catch(Exception error) { Report(error); } finally { busy=false; } await RefreshState(); if(!preview)poll.Start(); };
            initializing=false;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowTheme.Apply(Handle); }
        static Label Label(string text,float size,Color color,bool bold) { return new Label { Text=text,AutoSize=true,Dock=DockStyle.Fill,ForeColor=color,Font=new Font("Segoe UI",size,bold ? FontStyle.Bold : FontStyle.Regular),Margin=Padding.Empty }; }
        static LinkLabel Link(string text,Action action) { var link=new LinkLabel { Text=text,AutoSize=true,Anchor=AnchorStyles.Right,LinkColor=Theme.Muted,ActiveLinkColor=Theme.Text,VisitedLinkColor=Theme.Muted,LinkBehavior=LinkBehavior.HoverUnderline,Margin=new Padding(16,6,0,6) }; link.LinkClicked+=delegate { action(); }; return link; }
        void AddOption(TableLayoutPanel grid,OptionCard box,string title,string description,int col,int row,bool value) { box.Text=title; box.Description=description; box.Checked=value; box.Dock=DockStyle.Fill; box.Margin=new Padding(col==0?0:6,0,col==0?6:0,12); box.AccessibleDescription=description; grid.Controls.Add(box,col,row); }
        void StyleButton(ProfileButton b,string text) { b.Text=text; b.Dock=DockStyle.Fill; b.Height=50; b.Font=new Font(Font.FontFamily,11,FontStyle.Bold); }
        ProfileOptions SelectedOptions() { return new ProfileOptions { GameMode=gameMode.Checked,DisableCapture=capture.Checked,DisableMouseAcceleration=mouse.Checked,CpuReady=cpu.Checked,PcieReady=pcie.Checked,Timer=timer.Checked }; }
        void OptionsEnabled(bool value) { foreach(var box in new [] {gameMode,capture,mouse,cpu,pcie,timer})box.Enabled=value; }
        public void Reveal() { Show(); WindowState=FormWindowState.Normal; Activate(); }
        async Task ChangeProfile(bool on) {
            if(busy)return;
            if(on&&!preview&&Process.GetProcessesByName("68WAntiLagApp").Length>0) { MessageBox.Show(this,"Сначала закройте 68W AntiLag. Его прежние настройки Open AntiLag не отменяет.","Запущен другой AntiLag"); return; }
            busy=true; enable.Enabled=disable.Enabled=startup.Enabled=false; OptionsEnabled(false); status.Text=on?"Применение профиля…":"Восстановление…";
            var options=SelectedOptions();
            try { await Task.Run(delegate { if(on)engine.Enable(options); else engine.Disable(); }); if(!preview)Program.Log(on?"Enabled profile":"Disabled profile"); }
            catch(Exception error) { Report(error); } finally { busy=false; }
            await RefreshState();
        }
        public async Task RefreshState() {
            if(busy)return; busy=true;
            try {
                bool active=await Task.Run(delegate { return engine.IsActive; }); bool off=engine.State.Phase=="Disabled", recovery=!off&&engine.State.Phase!="Enabled";
                status.Text=off?"Готов к включению":recovery?"Нужно восстановление":active?"Профиль включён":"Часть настроек изменена";
                detail.Text=off?"Выберите настройки ниже и включите профиль.":recovery?"Операция не завершена. Нажмите «Восстановить».":active?"Чтобы изменить набор настроек, сначала отключите профиль.":"При восстановлении ваш внешний выбор будет сохранён.";
                enable.Enabled=off; disable.Enabled=!off; disable.Text=recovery?"Восстановить":"Отключить"; OptionsEnabled(off); startup.Enabled=true;
                tray.Text="Open AntiLag — "+(active?"включён":off?"выключен":"проверить состояние");
            } catch(Exception error) { status.Text="Ошибка проверки"; detail.Text=error.Message; enable.Enabled=false; disable.Enabled=engine.State.Phase!="Disabled"; }
            finally { busy=false; }
        }
        void Report(Exception error) { if(!preview)Program.Log(error.ToString()); Reveal(); MessageBox.Show(this,error.Message+"\n\nПодробности сохранены в журнале.","Open AntiLag: ошибка",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        protected override void Dispose(bool disposing) { if(disposing) { poll.Dispose(); tray.Dispose(); engine.Dispose(); } base.Dispose(disposing); }
    }
}
