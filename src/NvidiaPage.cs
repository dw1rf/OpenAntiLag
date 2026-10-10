using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class NvidiaPage : TabPage {
        readonly ComboBox mode=new ThemedComboBox {Name="gpuMode",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        readonly NumericUpDown fps=new ThemedNumericUpDown {Minimum=20,Maximum=1000,Value=141,Width=85,AccessibleName="Лимит FPS для G-SYNC"};
        readonly CheckBox power=new CheckBox {Text="Максимальное питание (больше нагрев)",AutoSize=true};
        readonly CheckBox quality=new CheckBox {Text="Фильтрация High Performance (хуже качество)",AutoSize=true};
        readonly TextBox body=new TextBox {Name="gpuGuide",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
        readonly TextBox status=new TextBox {Name="gpuStatus",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Height=65,BorderStyle=BorderStyle.None};
        readonly ProfileButton apply=new ProfileButton {Name="applyNvidia",Text="Применить NVIDIA",Primary=true},restore=new ProfileButton {Name="restoreNvidia",Text="Восстановить NVIDIA"},read=new ProfileButton {Text="Прочитать текущие"};
        readonly NvidiaController controller;
        readonly Func<bool> acquire;
        readonly Action release;
        readonly bool preview, logOperations;
        bool running,loaded;
        public NvidiaPage(bool preview,Func<bool> acquire,Action release,NvidiaController controller=null):base("NVIDIA · Глобальные") {
            this.preview=preview;this.acquire=acquire;this.release=release;logOperations=controller==null&&!preview;
            this.controller=controller??new NvidiaController(()=>new NvidiaDriver(),new NvidiaStore(Path.Combine(Program.DataDirectory,"nvidia-backup.xml")));
            BackColor=Theme.Background;ForeColor=Theme.Text;Padding=new Padding(18);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mode.Items.AddRange(new object[]{"Минимальная задержка · допустимы разрывы","Без разрывов · G-SYNC (монитор настроен)"});mode.SelectedIndex=0;
            var options=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
            var cap=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Top};
            cap.Controls.Add(new Label {Text="FPS для G-SYNC (ниже герцовки):",AutoSize=true,Margin=new Padding(0,6,6,0)});cap.Controls.Add(fps);
            options.Controls.Add(cap);options.Controls.Add(power);options.Controls.Add(quality);
            body.BackColor=status.BackColor=Theme.Background;body.ForeColor=status.ForeColor=Theme.Text;
            body.Margin=new Padding(0,12,0,12);status.Margin=new Padding(0,8,0,4);
            var actions=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,ColumnCount=2};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            foreach(var b in new[]{apply,restore,read}) {b.Dock=DockStyle.Fill;b.Height=44;}
            actions.Controls.Add(apply,0,0);actions.Controls.Add(restore,1,0);
            root.Controls.Add(mode,0,0);root.Controls.Add(options,0,1);root.Controls.Add(body,0,2);root.Controls.Add(read,0,3);root.Controls.Add(actions,0,4);root.Controls.Add(status,0,5);Controls.Add(root);
            mode.SelectedIndexChanged+=delegate {UpdatePlan();};fps.ValueChanged+=delegate {UpdatePlan();};power.CheckedChanged+=delegate {UpdatePlan();};quality.CheckedChanged+=delegate {UpdatePlan();};
            apply.Click+=async delegate {await Run(1);};restore.Click+=async delegate {await Run(2);};read.Click+=async delegate {await Run(0);};
            status.Text=preview?"Предпросмотр. Запись в драйвер отключена.":"Применение и восстановление меняют только глобальные параметры NVIDIA. Закройте игры перед применением.";
            apply.Enabled=restore.Enabled=read.Enabled=!preview;UpdatePlan();
            VisibleChanged+=async delegate {if(Visible&&!loaded&&!preview){loaded=true;await Run(0);}};
        }
        void UpdatePlan() {
            fps.Enabled=!running&&mode.SelectedIndex==1;
            body.Text=(mode.SelectedIndex==1?"РЕЖИМ: БЕЗ РАЗРЫВОВ":"РЕЖИМ: МИНИМАЛЬНАЯ ЗАДЕРЖКА")+"\r\n\r\n"+NvidiaPreset.Description(mode.SelectedIndex==1,(int)fps.Value,power.Checked,quality.Checked)+"\r\nПеред записью сохраняется резервная копия. «Восстановить NVIDIA» возвращает исходные значения, сохраняя отличающиеся внешние изменения. Для другого набора сначала выполните восстановление.\r\n";
        }
        async Task Run(int operation) {
            if(running||preview)return;
            if(!acquire()){status.Text="Дождитесь завершения другой операции Open AntiLag.";return;}
            running=true;apply.Enabled=restore.Enabled=read.Enabled=mode.Enabled=power.Enabled=quality.Enabled=fps.Enabled=false;
            var settings=NvidiaPreset.Build(mode.SelectedIndex==1,(int)fps.Value,power.Checked,quality.Checked);
            status.Text=operation==0?"Читаем NVIDIA…":operation==1?"Сохраняем исходные значения и применяем NVIDIA…":"Восстанавливаем NVIDIA…";
            try {
                string result=await Task.Run(()=>operation==0?controller.Inspect(settings):operation==1?controller.Apply(settings):controller.Restore());
                if(!IsDisposed){if(operation==0){body.Text=result+"\r\n\r\n"+NvidiaPreset.Description(mode.SelectedIndex==1,(int)fps.Value,power.Checked,quality.Checked);status.Text="Текущие значения прочитаны. Выбранный набор показан ниже них.";}else {status.Text=result;if(logOperations)Program.Log(result);}}
            } catch(Exception ex) {if(!IsDisposed)status.Text=ex.Message;if(logOperations)Program.Log("NVIDIA: "+ex.Message);}
            finally {running=false;release();if(!IsDisposed){apply.Enabled=restore.Enabled=read.Enabled=mode.Enabled=power.Enabled=quality.Enabled=true;fps.Enabled=mode.SelectedIndex==1;}}
        }
    }
}
