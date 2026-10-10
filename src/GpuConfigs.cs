using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OpenAntiLag {
    public static class GpuConfigs {
        public static string Guide(bool amd, bool smooth) {
            if(!amd)return NvidiaGlobalGuide.Get(smooth).Replace("\r\n","\n").Replace("\n","\r\n");
            return AmdProfiles.Guide(smooth,true);
        }
        public static TabPage Page(bool amd) {
            var page = new TabPage(amd ? "AMD" : "NVIDIA · Глобальные") { BackColor=Theme.Background, Padding=new Padding(18) };
            var layout = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var mode = new ComboBox { Name="gpuMode", DropDownStyle=ComboBoxStyle.DropDownList, Dock=DockStyle.Top, BackColor=Theme.Surface, ForeColor=Theme.Text };
            mode.Items.AddRange(new object[] { "Минимальная задержка · допустимы разрывы", "Без разрывов · G-SYNC / FreeSync" }); mode.SelectedIndex=0;
            var body = new TextBox { Name="gpuGuide", Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Fill, BackColor=Theme.Background, ForeColor=Theme.Text, BorderStyle=BorderStyle.None, Text=Guide(amd,false), Margin=new Padding(0,16,0,16) };
            mode.SelectedIndexChanged += delegate { body.Text=Guide(amd,mode.SelectedIndex==1); body.SelectionStart=0; body.ScrollToCaret(); };
            var save = new ProfileButton { Text="Сохранить конфиг (.txt)", Dock=DockStyle.Top, Height=44 };
            save.Click += delegate {
                using(var dialog=new SaveFileDialog { Filter="Текстовый конфиг (*.txt)|*.txt", FileName=(amd?"AMD":"NVIDIA-617.42")+(mode.SelectedIndex==1?"-VRR":"-Esports")+".txt" }) {
                    if(dialog.ShowDialog(page)!=DialogResult.OK)return;
                    try { File.WriteAllText(dialog.FileName,body.Text,new UTF8Encoding(true)); }
                    catch(Exception error) { MessageBox.Show(page,error.Message,"Не удалось сохранить",MessageBoxButtons.OK,MessageBoxIcon.Error); }
                }
            };
            layout.Controls.Add(mode,0,0); layout.Controls.Add(body,0,1); layout.Controls.Add(save,0,2); page.Controls.Add(layout); return page;
        }
    }
}
