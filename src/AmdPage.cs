using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class AmdPage:TabPage {
        readonly ComboBox devices=new ComboBox {Name="amdDevice",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        readonly ComboBox mode=new ComboBox {Name="gpuMode",DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
        readonly CheckBox anti=new CheckBox {Text="Обычный Radeon Anti-Lag (без Anti-Lag 2 в игре)",AutoSize=true,Checked=true};
        readonly TextBox body=new TextBox {Name="gpuGuide",Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None};
        readonly TextBox status=new TextBox {Name="amdStatus",Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Height=70,BorderStyle=BorderStyle.None};
        readonly ProfileButton apply=new ProfileButton {Name="applyAmd",Text="Применить AMD",Primary=true},restore=new ProfileButton {Name="restoreAmd",Text="Восстановить AMD"},read=new ProfileButton {Text="Прочитать AMD"};
        readonly AmdProfiles controller;readonly Func<bool> acquire;readonly Action release;readonly bool preview,logging;
        bool running,loaded,ready;
        public AmdPage(bool preview,Func<bool> acquire,Action release,AmdProfiles controller=null):base("AMD · Глобальные") {
            this.preview=preview;this.acquire=acquire;this.release=release;logging=controller==null&&!preview;
            this.controller=controller??new AmdProfiles(()=>new AmdDriver(),new AmdStore(Path.Combine(Program.DataDirectory,"amd-backup.xml")));
            BackColor=Theme.Background;ForeColor=Theme.Text;Padding=new Padding(18);
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=7};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=0;i<7;i++)root.RowStyles.Add(new RowStyle(i==3?SizeType.Percent:SizeType.AutoSize,i==3?100:0));
            mode.Items.AddRange(new object[]{"Минимальная задержка · разрывы допустимы","Без разрывов · FreeSync настраивается отдельно"});mode.SelectedIndex=0;
            if(preview){devices.Items.Add(new AmdGpu {Id="preview",Name="AMD Radeon — предпросмотр"});devices.SelectedIndex=0;}
            body.BackColor=status.BackColor=Theme.Background;body.ForeColor=status.ForeColor=Theme.Text;body.Margin=new Padding(0,10,0,10);status.Text=preview?"Предпросмотр. Драйвер не изменяется.":"Экспериментальная поддержка ADLX. Сначала прочитайте доступные GPU и параметры.";
            var buttons=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            foreach(var b in new[]{apply,restore,read}){b.Dock=DockStyle.Fill;b.Height=44;}buttons.Controls.Add(apply,0,0);buttons.Controls.Add(restore,1,0);
            root.Controls.Add(devices,0,0);root.Controls.Add(mode,0,1);root.Controls.Add(anti,0,2);root.Controls.Add(body,0,3);root.Controls.Add(read,0,4);root.Controls.Add(buttons,0,5);root.Controls.Add(status,0,6);Controls.Add(root);
            mode.SelectedIndexChanged+=delegate{Plan();};anti.CheckedChanged+=delegate{Plan();};devices.SelectedIndexChanged+=delegate{Plan();};
            apply.Click+=async delegate{await Run(1);};restore.Click+=async delegate{await Run(2);};read.Click+=async delegate{await Run(0);};
            VisibleChanged+=async delegate{if(Visible&&!loaded&&!preview){loaded=true;await Run(0);}};
            apply.Enabled=restore.Enabled=false;read.Enabled=!preview;Plan();
        }
        void Plan(){body.Text=AmdProfiles.Guide(mode.SelectedIndex==1,anti.Checked);}
        async Task Run(int operation){if(preview||running)return;if(!acquire()){status.Text="Дождитесь завершения другой операции.";return;}running=true;apply.Enabled=restore.Enabled=read.Enabled=devices.Enabled=mode.Enabled=anti.Enabled=false;
            bool sync=mode.SelectedIndex==1,useAnti=anti.Checked;string gpu=devices.SelectedItem==null?null:((AmdGpu)devices.SelectedItem).Id;
            try {status.Text="Обращение к драйверу AMD…";
                if(operation==0){var list=await Task.Run(()=>controller.Devices());if(IsDisposed)return;devices.Items.Clear();foreach(var d in list)devices.Items.Add(d);devices.SelectedIndex=list.FindIndex(d=>d.Id==gpu);if(devices.SelectedIndex<0&&list.Count>0)devices.SelectedIndex=0;gpu=devices.SelectedItem==null?null:((AmdGpu)devices.SelectedItem).Id;if(gpu==null)throw new NotSupportedException("Совместимая Radeon не найдена.");}
                string result=await Task.Run(()=>operation==0?controller.Inspect(gpu):operation==1?controller.Apply(gpu,useAnti,sync):controller.Restore());ready=true;
                if(!IsDisposed){if(operation==0){body.Text=result+"\r\n"+AmdProfiles.Guide(sync,useAnti);status.Text="Параметры прочитаны. Неподдерживаемые функции будут пропущены.";}else status.Text=result;}if(logging&&operation!=0)Program.Log(result);
            }catch(Exception ex){if(operation==0)ready=false;if(!IsDisposed)status.Text=ex.Message;if(logging)Program.Log("AMD: "+ex.Message);}
            finally{running=false;release();if(!IsDisposed){devices.Enabled=mode.Enabled=anti.Enabled=read.Enabled=true;apply.Enabled=restore.Enabled=ready;}}
        }
    }
}
