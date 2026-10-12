using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class MainForm : Form {
        readonly Engine engine;
        readonly ISystemClient system;
        readonly ProfileController controller;
        readonly bool preview, startHidden;
        readonly Label status=new Label(), detail=new Label();
        readonly OptionCard gameMode=new OptionCard(), capture=new OptionCard(), mouse=new OptionCard(), cpu=new OptionCard(), pcie=new OptionCard(), timer=new OptionCard(), startup=new OptionCard { Compact=true };
        readonly OptionCard[] advanced=MachineSettings.Ids.Select(id=>new OptionCard {Name="advanced_"+id}).ToArray();
        readonly ProfileButton enable=new ProfileButton { Primary=true }, disable=new ProfileButton(), restoreBasic=new ProfileButton {Name="restoreBasic"};
        readonly TextBox recoveryResult=new TextBox {Name="recoveryResult",Multiline=true,ReadOnly=true,Height=44,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Theme.Background,ForeColor=Theme.Muted};
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer poll;
        readonly Panel scroll=new Panel { Name="profileOptionsScroll",Dock=DockStyle.Fill, AutoScroll=true };
        readonly TableLayoutPanel content=new TableLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Dock=DockStyle.Top, ColumnCount=1, Margin=Padding.Empty };
        bool gpuBusy, busy, readingState, exiting, initializing=true, checkingUpdate;
        readonly Label updateStatus=new Label();
        readonly OptionCard autoUpdate=new OptionCard {Compact=true};
        UpdatePackage pendingUpdate;
        DateTime nextUpdate=DateTime.UtcNow;
        public MainForm(Engine engine,bool startHidden,bool preview,ISystemClient systemClient=null) {
            this.engine=engine; this.preview=preview; this.startHidden=startHidden;
            system=systemClient ?? (preview ? (ISystemClient)new PreviewSystemClient() : new SystemClient());
            controller=new ProfileController(engine,system);
            Text="Open AntiLag"; Font=new Font("Segoe UI",10); BackColor=Theme.Background; ForeColor=Theme.Text;
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(800,930); MinimumSize=new Size(640,570); StartPosition=FormStartPosition.CenterScreen; Icon=WindowTheme.AppIcon();
            var root=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(24), ColumnCount=1, RowCount=5, BackColor=Theme.Background };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            var header=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=2, Margin=new Padding(0,0,0,20) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Label("Open AntiLag",24,Theme.Text,true),0,0);
            var version=Label("v0.6.4",10,Theme.Muted,false); version.Anchor=AnchorStyles.Right; header.Controls.Add(version,1,0);
            var subtitle=Label("Твой игровой профиль. Под контролем.",10,Theme.Muted,false); header.Controls.Add(subtitle,0,1); header.SetColumnSpan(subtitle,2); root.Controls.Add(header,0,0);
            var stateCard=new GradientCard { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=1, BackColor=Theme.Background, Padding=new Padding(18,14,18,14), Margin=new Padding(0,0,0,20) };
            stateCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            status.BackColor=Color.Transparent; detail.BackColor=Color.Transparent; status.AutoSize=true; status.Dock=DockStyle.Fill; status.Text="Проверка состояния…"; status.Font=new Font(Font.FontFamily,16,FontStyle.Bold); status.ForeColor=Theme.Text; status.Margin=new Padding(0,0,0,6);
            detail.AutoSize=true; detail.Dock=DockStyle.Fill; detail.Text="Читаем сохранённый профиль."; detail.ForeColor=Theme.Text; detail.Margin=Padding.Empty;
            stateCard.Controls.Add(status); stateCard.Controls.Add(detail); root.Controls.Add(stateCard,0,1);
            root.Controls.Add(scroll,0,2); scroll.Controls.Add(content); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            scroll.HandleCreated+=delegate { WindowTheme.Scrollbar(scroll.Handle); };
            var plan=Label("Профиль производительности",12,Theme.Text,true); plan.Margin=new Padding(0,0,0,6); content.Controls.Add(plan);
            var powerNote=Label("Сохраняет текущие параметры питания и применяет выбранные опции.\nСистемные изменения — только по отдельным галочкам ниже.",10,Theme.Muted,false); powerNote.Margin=new Padding(0,0,0,16); content.Controls.Add(powerNote);
            var grid=new TableLayoutPanel { AutoSize=true, AutoSizeMode=AutoSizeMode.GrowAndShrink, Dock=DockStyle.Top, ColumnCount=2, Margin=Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            var options=engine.State.Phase=="Disabled" ? ProfileOptions.Gaming() : engine.State.Options;
            AddOption(grid,gameMode,"Игровой режим Windows","Включить Game Mode для игр.",0,0,options.GameMode);
            AddOption(grid,capture,"Отключить запись Xbox","Без фоновой записи и игровых клипов.",1,0,options.DisableCapture);
            AddOption(grid,mouse,"Мышь без ускорения","Равномерное движение курсора. Raw Input не меняется.",0,1,options.DisableMouseAcceleration);
            AddOption(grid,cpu,"CPU без парковки","От сети: минимум CPU 100%. Больше нагрев и расход.",1,1,options.CpuReady);
            AddOption(grid,pcie,"Питание PCIe","Отключить энергосбережение PCIe при питании от сети.",0,2,options.PcieReady);
            AddOption(grid,timer,"Таймер 1 мс","Эксперимент. Эффект в играх не гарантирован.",1,2,engine.State.TimerRequested); content.Controls.Add(grid);
            var note=Label("CPU, PCIe и таймер — дополнительные опции, по умолчанию выключены. Могут повысить нагрев и расход энергии. Прирост FPS не гарантирован.",9.5f,Theme.Muted,false); note.Margin=new Padding(0,2,0,12); content.Controls.Add(note);
            var advancedTitle=Label("Дополнительные системные настройки",12,Theme.Text,true);advancedTitle.Margin=new Padding(0,12,0,8);content.Controls.Add(advancedTitle);
            var advancedNote=Label("По умолчанию выключены. Нужны UAC и перезагрузка после изменения/отката. BCD — отладочные параметры: возможны сбои и проблемы загрузки. HAGS требует поддержки GPU и драйвера; запись значения не подтверждает активацию.",9.5f,Theme.Muted,false);advancedNote.Margin=new Padding(0,0,0,10);content.Controls.Add(advancedNote);
            var advancedGrid=new TableLayoutPanel {AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=1,Margin=Padding.Empty};advancedGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            string[] titles={"BCD: автоматический источник часов","BCD: платформенный тик","BCD: отключить динамический тик","Включить HAGS","Приоритет активных приложений"};
            string[] descriptions={"Удалить принудительный useplatformclock. Не отключает устройство HPET.","useplatformtick = yes. Экспериментальная отладочная настройка.","disabledynamictick = yes. Может увеличить расход энергии.","Аппаратное планирование GPU: HwSchMode = 2. После перезагрузки.","Win32PrioritySeparation = 42 (0x2A). Эффект зависит от нагрузки."};
            MachineState savedMachine;
            try {savedMachine=system.State;}catch(Exception error){savedMachine=new MachineState();recoveryResult.Text="Системный журнал недоступен: "+error.Message+" Основные параметры можно восстановить отдельно.";}
            for(int i=0;i<advanced.Length;i++){advancedGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));AddOption(advancedGrid,advanced[i],titles[i],descriptions[i],0,i,savedMachine.Version==2&&savedMachine.Phase!="Disabled"&&savedMachine.Entries.Any(x=>x.Id==MachineSettings.Ids[i]));advanced[i].Dock=DockStyle.Top;}
            content.Controls.Add(advancedGrid);
            var actions=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=2, Margin=new Padding(0,16,0,12) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            StyleButton(enable,"Включить профиль"); StyleButton(disable,"Отключить"); enable.Name="enable"; disable.Name="disable"; enable.Margin=new Padding(0,0,6,0); disable.Margin=new Padding(6,0,0,0);
            enable.Enabled=disable.Enabled=false; actions.Controls.Add(enable); actions.Controls.Add(disable); root.Controls.Add(actions,0,3);
            StyleButton(restoreBasic,"Восстановить основные");restoreBasic.Height=44;actions.Controls.Add(restoreBasic,0,1);actions.SetColumnSpan(restoreBasic,2);
            recoveryResult.HandleCreated+=delegate{WindowTheme.Scrollbar(recoveryResult.Handle);};
            recoveryResult.Margin=new Padding(0,6,0,0);if(String.IsNullOrEmpty(recoveryResult.Text))recoveryResult.Text="Восстановление основных: питание, Game Mode, запись Xbox, мышь и таймер. HAGS/BCD и GPU восстанавливаются отдельно.";
            actions.Controls.Add(recoveryResult,0,2);actions.SetColumnSpan(recoveryResult,2);
            restoreBasic.Click+=async delegate {await RestoreBasic();};
            enable.Click+=async delegate { await ChangeProfile(true); }; disable.Click+=async delegate { await ChangeProfile(false); };
            var footer=new TableLayoutPanel { Dock=DockStyle.Fill, AutoSize=true, ColumnCount=3, Margin=Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            startup.Text="Запускать с Windows"; startup.Dock=DockStyle.Fill; startup.Checked=!preview && Startup.Enabled; startup.Margin=Padding.Empty; footer.Controls.Add(startup,0,0);
            startup.CheckedChanged+=delegate { if(initializing||preview)return; try { Startup.Enabled=startup.Checked; } catch(Exception error) { initializing=true; startup.Checked=!startup.Checked; initializing=false; Report(error); } };
            footer.Controls.Add(Link("Журнал",delegate { if(!preview) { Directory.CreateDirectory(Program.DataDirectory); Process.Start("explorer.exe","\""+Program.DataDirectory+"\""); } }),1,0);
            footer.Controls.Add(Link("О программе",delegate { MessageBox.Show(this,"Open AntiLag 0.6.4 • MIT\nМодуль шейдеров: отдельный SCSKiller 1.2.5, GPL-3.0-or-later и исключения.\nhttps://github.com/BlueHeisenberg/SCSKiller\n\nИсходные значения сохраняются до изменений. Внешние изменения пользователя при откате сохраняются.\n\nПрофиль не гарантирует прирост FPS или нулевую задержку. Настройки Xbox зависят от версии Windows. Таймер 1 мс не является универсальной оптимизацией игр.\n\nBCD, HAGS и системные приоритеты доступны по отдельным галочкам, выключенным по умолчанию. Требуют UAC и перезагрузки. Старые профили можно восстановить. Питание копируется из текущего плана; дополнительные опции требуют отдельного выбора.","Open AntiLag",MessageBoxButtons.OK,MessageBoxIcon.Information); }),2,0); root.Controls.Add(footer,0,4);
            autoUpdate.Text="Автообновления"; autoUpdate.Dock=DockStyle.Fill; autoUpdate.Margin=Padding.Empty;autoUpdate.Checked=preview||Updates.Enabled;
            footer.Controls.Add(autoUpdate,0,1);
            autoUpdate.CheckedChanged+=delegate {if(initializing||preview)return;try {Updates.Enabled=autoUpdate.Checked;if(!autoUpdate.Checked)pendingUpdate=null;}catch(Exception error){Report(error);} };
            var checkUpdates=Link("Проверить",async delegate {if(!preview)await CheckUpdate(true);});footer.Controls.Add(checkUpdates,1,1);footer.SetColumnSpan(checkUpdates,2);
            updateStatus.Text="Обновления из GitHub Releases";updateStatus.ForeColor=Theme.Muted;updateStatus.Font=new Font(Font.FontFamily,9);updateStatus.AutoSize=true;updateStatus.Dock=DockStyle.Fill;updateStatus.Margin=new Padding(0,2,0,0);footer.Controls.Add(updateStatus,0,2);footer.SetColumnSpan(updateStatus,3);
            var menu=new ContextMenuStrip { BackColor=Theme.Surface, ForeColor=Theme.Text };
            menu.Items.Add("Открыть",null,delegate { Reveal(); });
            menu.Items.Add("Выход",null,delegate { if(busy||gpuBusy)return; if(MessageBox.Show(this,"Выход не отключает профиль. Для отката сначала нажмите «Отключить».\n\nВыйти?","Open AntiLag",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return; exiting=true; Close(); });
            tray=new NotifyIcon { Icon=Icon,Text="Open AntiLag",ContextMenuStrip=menu,Visible=!preview }; tray.DoubleClick+=delegate { Reveal(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(!exiting && !preview && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
            Resize+=delegate {bool compact=ClientSize.Height<700;header.Visible=!compact;detail.Visible=!compact;root.Padding=new Padding(compact?12:24);header.Margin=new Padding(0,0,0,compact?6:20);subtitle.Visible=!compact;stateCard.Padding=new Padding(compact?12:18,compact?8:14,compact?12:18,compact?8:14);stateCard.Margin=new Padding(0,0,0,compact?8:20);};
            Resize+=delegate { if(WindowState==FormWindowState.Minimized&&!preview)Hide(); };
            poll=new System.Windows.Forms.Timer { Interval=5000 }; poll.Tick+=async delegate { if(!busy&&Visible)await RefreshState(); if(!busy) {TryInstallUpdate(); if(Updates.Enabled&&DateTime.UtcNow>=nextUpdate)await CheckUpdate(false);} };
            Shown+=async delegate { if(startHidden)Hide(); busy=true; try { await Task.Run((Action)controller.Resume); } catch(Exception error) { Report(error); } finally { busy=false; } await RefreshState(); if(!preview) {if(disable.Text=="Восстановить")Reveal();poll.Start();if(Updates.Enabled)await CheckUpdate(false);} };
            var tabs=new ThemedTabs { Name="mainTabs", Dock=DockStyle.Fill };
            var systemPage=new TabPage("Система") { BackColor=Theme.Background };
            Controls.Remove(root); systemPage.Controls.Add(root); tabs.TabPages.Add(systemPage); tabs.TabPages.Add(new NvidiaPage(preview,TryAcquireGpu,ReleaseGpu)); tabs.TabPages.Add(new AmdPage(preview,TryAcquireGpu,ReleaseGpu)); Controls.Add(tabs);
            tabs.TabPages.Add(new ShaderPage(preview,TryAcquireGpu,ReleaseGpu));
            initializing=false;
        }
        internal bool TryAcquireGpu() {if((busy&&!readingState)||gpuBusy||exiting)return false;gpuBusy=true;return true;}
        internal void ReleaseGpu() {gpuBusy=false;}
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowTheme.Apply(Handle); }
        static Label Label(string text,float size,Color color,bool bold) { return new Label { Text=text,AutoSize=true,Dock=DockStyle.Fill,ForeColor=color,Font=new Font("Segoe UI",size,bold ? FontStyle.Bold : FontStyle.Regular),Margin=Padding.Empty }; }
        static LinkLabel Link(string text,Action action) { var link=new LinkLabel { Text=text,AutoSize=true,Anchor=AnchorStyles.Right,LinkColor=Theme.Muted,ActiveLinkColor=Theme.Text,VisitedLinkColor=Theme.Muted,LinkBehavior=LinkBehavior.HoverUnderline,Margin=new Padding(16,6,0,6) }; link.LinkClicked+=delegate { action(); }; return link; }
        void AddOption(TableLayoutPanel grid,OptionCard box,string title,string description,int col,int row,bool value) { box.Text=title; box.Description=description; box.Checked=value; box.Dock=DockStyle.Fill; box.Margin=new Padding(col==0?0:6,0,col==0?6:0,12); box.AccessibleDescription=description; grid.Controls.Add(box,col,row); }
        void StyleButton(ProfileButton b,string text) { b.Text=text; b.Dock=DockStyle.Fill; b.Height=50; b.Font=new Font(Font.FontFamily,11,FontStyle.Bold); }
        ProfileOptions SelectedOptions() { return new ProfileOptions { GameMode=gameMode.Checked,DisableCapture=capture.Checked,DisableMouseAcceleration=mouse.Checked,CpuReady=cpu.Checked,PcieReady=pcie.Checked,Timer=timer.Checked }; }
        void OptionsEnabled(bool value) { foreach(var box in new [] {gameMode,capture,mouse,cpu,pcie,timer}.Concat(advanced))box.Enabled=value; }
        public void Reveal() { Show(); WindowState=FormWindowState.Normal; Activate(); }
        async Task WaitForRead() {if(readingState)recoveryResult.Text="Ожидаем ответа Windows на проверку состояния. Выбор галочек остаётся доступным.";while(readingState&&!IsDisposed)await Task.Delay(50);}
        void ResetBasicOptions() {
            var defaults=ProfileOptions.Gaming();gameMode.Checked=defaults.GameMode;capture.Checked=defaults.DisableCapture;mouse.Checked=defaults.DisableMouseAcceleration;cpu.Checked=pcie.Checked=timer.Checked=false;
        }
        async Task RestoreBasic() {
            await WaitForRead();if(IsDisposed)return;
            if(busy||gpuBusy){recoveryResult.Text="Сейчас выполняется другая операция. Дождитесь её завершения и повторите восстановление.";return;}
            bool hadBackup=engine.State.Phase!="Disabled";busy=true;enable.Enabled=disable.Enabled=restoreBasic.Enabled=false;OptionsEnabled(false);
            recoveryResult.Text="Восстанавливаем основные параметры из сохранённой копии…";
            try {
                await Task.Run((Action)engine.Disable);ResetBasicOptions();
                recoveryResult.Text=hadBackup?"Основные параметры восстановлены. Базовые галочки сброшены; причина оставшейся блокировки показана выше.":"Сохранённого основного профиля нет: настройки Windows не менялись. Галочки возвращены к начальному выбору.";
                if(!preview)Program.Log("Restored basic profile only");
            }catch(Exception error){recoveryResult.Text="Восстановление основных не завершено: "+error.Message;if(!preview)Program.Log(recoveryResult.Text);}
            finally {busy=false;restoreBasic.Enabled=true;}
            await RefreshState();
        }
        async Task ChangeProfile(bool on) {
            await WaitForRead();if(IsDisposed)return;
            if(busy||gpuBusy){recoveryResult.Text="Дождитесь завершения операции на другой вкладке и повторите действие.";return;}
            if(on&&!preview&&Process.GetProcessesByName("68WAntiLagApp").Length>0) { MessageBox.Show(this,"Сначала закройте 68W AntiLag. Его прежние настройки Open AntiLag не отменяет.","Запущен другой AntiLag"); return; }
            busy=true; enable.Enabled=disable.Enabled=restoreBasic.Enabled=startup.Enabled=false; OptionsEnabled(false); status.Text=on?"Применение профиля…":"Восстановление…";
            var options=SelectedOptions();var selected=advanced.Select((box,i)=>new {box,i}).Where(x=>x.box.Checked).Select(x=>MachineSettings.Ids[x.i]).ToArray();
            try { await Task.Run(delegate { if(on)controller.Enable(options,selected); else controller.Disable(); }); if(!preview)Program.Log(on?"Enabled profile; selected system settings: "+String.Join(",",selected):"Restored profile"); }
            catch(Exception error) { recoveryResult.Text=error.Message;Report(error); } finally { busy=false;restoreBasic.Enabled=true; }
            if(!on && engine.State.Phase=="Disabled") {
                ResetBasicOptions();try{if(system.State.Phase=="Disabled")foreach(var box in advanced)box.Checked=false;}catch(Exception error){recoveryResult.Text=error.Message;}
            }
            await RefreshState();
        }
        public async Task RefreshState() {
            if(busy||gpuBusy)return; busy=true;readingState=true;
            try {
                // Registry/WMI/driver status must never run on the UI thread.
                var snapshot=await Task.Run(delegate {var state=system.State;return new {Machine=state,Active=engine.IsActive,Reboot=system.RebootPending};});
                var machine=snapshot.Machine;
                bool active=snapshot.Active;
                bool off=engine.State.Phase=="Disabled"&&machine.Phase=="Disabled";
                bool legacy=(machine.Phase!="Disabled"&&machine.Version==1) || (engine.State.Phase!="Disabled" && engine.State.SchemaVersion<3);
                bool recovery=legacy || (!off && engine.State.Phase!="Enabled") || (machine.Phase!="Disabled"&&machine.Phase!="Enabled");
                bool reboot=snapshot.Reboot;
                status.Text=legacy?"Восстановите старый профиль":recovery?"Нужно восстановление":reboot?"Нужна перезагрузка ПК":off?"Готов к включению":active?"Профиль включён":"Часть настроек изменена";
                detail.Text=legacy?"Сохранён профиль прошлой версии. Выбор галочек доступен. Перед применением нажмите «Восстановить». Для системных изменений потребуются права администратора и перезагрузка.":recovery?"Операция не завершена. Нажмите «Восстановить».":reboot?"Галочки можно выбрать сейчас. Применение нового профиля доступно после перезагрузки Windows.":off?"Базовый профиль сохраняет параметры питания. Дополнительные системные параметры меняются только по выбранным галочкам.":active?"Выбранные опции применены. Для нового набора сначала нажмите «Отключить» или «Восстановить основные».":"При восстановлении ваш внешний выбор будет сохранён.";
                enable.Enabled=true; disable.Enabled=true; disable.Text=recovery?"Восстановить":"Отключить"; OptionsEnabled(true); startup.Enabled=true;
                tray.Text="Open AntiLag — "+(active?"включён":off?"выключен":"проверить состояние");
            } catch(Exception error) { status.Text="Ошибка проверки"; detail.Text=error.Message+" Основные параметры можно восстановить отдельной кнопкой ниже."; enable.Enabled=true; disable.Enabled=true;OptionsEnabled(true); }
            finally { readingState=false;busy=false;restoreBasic.Enabled=true;OptionsEnabled(true); }
        }
        async Task CheckUpdate(bool manual) {
            if(checkingUpdate||exiting||IsDisposed)return;
            checkingUpdate=true;nextUpdate=DateTime.UtcNow.AddHours(6);
            updateStatus.Text="Проверка и загрузка обновлений…";
            try {
                var package=await Updates.Download();
                if(IsDisposed||exiting)return;
                if(!manual&&!autoUpdate.Checked) {updateStatus.Text="Автообновления выключены";return;}
                pendingUpdate=package;
                updateStatus.Text=package==null?"Установлена актуальная версия":"Версия "+package.Version.ToString(3)+" готова. Ожидание завершения операции…";
                TryInstallUpdate();
            } catch(Exception error) {Program.Log("Update check: "+error.Message);if(!IsDisposed)updateStatus.Text="Не удалось обновить. Повторная проверка позже.";}
            finally {checkingUpdate=false;}
        }
        void TryInstallUpdate() {
            if(pendingUpdate==null||busy||gpuBusy||exiting||IsDisposed)return;
            try {
                if(engine.State.Phase=="Applying"||engine.State.Phase=="Restoring"||system.State.Phase=="Applying"||system.State.Phase=="Restoring")return;
                Updates.StartInstall(pendingUpdate,!Visible);pendingUpdate=null;exiting=true;Close();
            }catch(Exception error){pendingUpdate=null;updateStatus.Text="Установка отложена. Подробности в журнале.";Program.Log("Update install: "+error);}
        }
        void Report(Exception error) { if(!preview)Program.Log(error.ToString()); Reveal(); MessageBox.Show(this,error.Message+"\n\nПодробности сохранены в журнале.","Open AntiLag: ошибка",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        protected override void Dispose(bool disposing) { if(disposing) { poll.Dispose(); tray.Dispose(); engine.Dispose(); } base.Dispose(disposing); }
    }
}
