using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenAntiLag {
    public sealed class ThemedTabs : TabControl {
        public ThemedTabs() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding=new Point(14,7);
        }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
        protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Theme.Background);
            for(int i=0;i<TabCount;i++) {
                Rectangle r=GetTabRect(i); bool selected=i==SelectedIndex;
                using(var brush=new SolidBrush(selected?Theme.Selected:Theme.Surface))e.Graphics.FillRectangle(brush,r);
                TextRenderer.DrawText(e.Graphics,TabPages[i].Text,Font,r,selected?Theme.Text:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
                if(selected)using(var brush=new SolidBrush(Theme.Accent))e.Graphics.FillRectangle(brush,r.Left,r.Bottom-3,r.Width,3);
                if(selected&&Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(r,-5,-5),Theme.Text,Theme.Selected);
            }
        }
    }
    public sealed class ThemedComboBox : ComboBox {
        public ThemedComboBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,true); BackColor=Theme.Surface;ForeColor=Theme.Text;FlatStyle=FlatStyle.Flat;DrawMode=DrawMode.OwnerDrawFixed; }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e);ItemHeight=Font.Height+6; }
        protected override void OnDrawItem(DrawItemEventArgs e) {
            bool selected=(e.State&DrawItemState.Selected)!=0;
            using(var brush=new SolidBrush(selected?Theme.Selected:Theme.Surface))e.Graphics.FillRectangle(brush,e.Bounds);
            string text=e.Index>=0?GetItemText(Items[e.Index]):Text;
            TextRenderer.DrawText(e.Graphics,text,Font,Rectangle.Inflate(e.Bounds,-4,0),Enabled?Theme.Text:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;
            g.Clear(Theme.Surface);
            TextRenderer.DrawText(g,Text,Font,new Rectangle(6,0,Math.Max(1,Width-SystemInformation.VerticalScrollBarWidth-12),Height),Enabled?Theme.Text:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            {
                var button=new Rectangle(Width-SystemInformation.VerticalScrollBarWidth-2,1,SystemInformation.VerticalScrollBarWidth+1,Height-2);
                using(var brush=new SolidBrush(Theme.Surface))g.FillRectangle(brush,button);
                int x=button.Left+button.Width/2,y=button.Top+button.Height/2;
                using(var pen=new Pen(Enabled?Theme.Text:Theme.Muted)) {g.DrawLine(pen,x-4,y-2,x,y+2);g.DrawLine(pen,x,y+2,x+4,y-2);}
                using(var pen=new Pen(Focused?Theme.Accent:Theme.Border))g.DrawRectangle(pen,0,0,Width-1,Height-1);
            }
        }
    }
    public sealed class ThemedNumericUpDown : NumericUpDown {
        public ThemedNumericUpDown() {
            BackColor=Theme.Surface;ForeColor=Theme.Text;BorderStyle=BorderStyle.FixedSingle;
            foreach(Control child in Controls)if(!(child is TextBox))child.Paint+=PaintButtons;
        }
        void PaintButtons(object sender,PaintEventArgs e) {
            var c=(Control)sender;e.Graphics.Clear(Theme.Surface);
            using(var pen=new Pen(Theme.Border))e.Graphics.DrawRectangle(pen,0,0,c.Width-1,c.Height-1);
            int x=c.Width/2;
            using(var pen=new Pen(Enabled?Theme.Text:Theme.Muted)) {
                int y=c.Height/4;e.Graphics.DrawLine(pen,x-3,y+2,x,y-1);e.Graphics.DrawLine(pen,x,y-1,x+3,y+2);
                y=c.Height*3/4;e.Graphics.DrawLine(pen,x-3,y-2,x,y+1);e.Graphics.DrawLine(pen,x,y+1,x+3,y-2);
            }
        }
    }
}
