using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenAntiLag {
    public static class Theme {
        public static readonly Color Background = Color.FromArgb(11,20,35);
        public static readonly Color Surface = Color.FromArgb(22,39,57);
        public static readonly Color Selected = Color.FromArgb(31,57,82);
        public static readonly Color Border = Color.FromArgb(55,78,100);
        public static readonly Color Accent = Color.FromArgb(144,176,199);
        public static readonly Color Text = Color.FromArgb(226,237,245);
        public static readonly Color Muted = Color.FromArgb(177,199,215);
        public static GraphicsPath Round(Rectangle r, int radius) {
            var p = new GraphicsPath(); int d = radius * 2;
            p.AddArc(r.Left,r.Top,d,d,180,90); p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.Left,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        public static void Fill(Graphics g, Rectangle r, Color fill, Color border, int radius) {
            if (r.Width < radius*2 || r.Height < radius*2) return;
            using(var p=Round(r,radius)) using(var b=new SolidBrush(fill)) using(var pen=new Pen(border)) { g.FillPath(b,p); g.DrawPath(pen,p); }
        }
    }
    public class ProfileButton : Button {
        public bool Primary;
        public ProfileButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill = Enabled && Primary ? Theme.Accent : Theme.Surface;
            Color text = Enabled && Primary ? Theme.Background : Theme.Muted;
            Theme.Fill(e.Graphics,new Rectangle(0,0,Width-1,Height-1),fill,Enabled ? Theme.Accent : Theme.Border,8);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-6,-6),text,fill);
        }
    }
    public sealed class OptionCard : CheckBox {
        public string Description { get; set; }
        public bool Compact { get; set; }
        float ScaleFactor { get { return Font.Size / 10.0f; } }
        int D(int x) { return (int)Math.Round(x*ScaleFactor); }
        public OptionCard() { AutoSize=true; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); Cursor=Cursors.Hand; AccessibleRole=AccessibleRole.CheckButton; }
        public override Size GetPreferredSize(Size proposed) {
            int width=proposed.Width > D(80) && proposed.Width < 3000 ? proposed.Width : D(310);
            int textWidth=Math.Max(D(50),width-D(66));
            int title=TextRenderer.MeasureText(Text,Font,new Size(textWidth,10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
            int description=0;
            using(var f=new Font(Font.FontFamily,Font.Size-0.5f)) if(!Compact) description=TextRenderer.MeasureText(Description ?? "",f,new Size(textWidth,10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
            return new Size(width,Math.Max(D(Compact ? 36 : 90),title+description+D(Compact ? 14 : 36)));
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            Color fill=Compact ? Theme.Background : Checked ? Theme.Selected : Theme.Surface;
            if(!Compact) Theme.Fill(e.Graphics,new Rectangle(0,0,Width-1,Height-1),fill,Checked ? Theme.Accent : Theme.Border,10);
            int pad=D(Compact ? 2 : 16), box=D(17), textX=pad+box+D(12);
            var check=new Rectangle(pad,D(Compact ? 10 : 18),box,box);
            using(var brush=new SolidBrush(Checked ? Theme.Accent : fill)) e.Graphics.FillRectangle(brush,check);
            using(var pen=new Pen(Theme.Accent)) e.Graphics.DrawRectangle(pen,check);
            if(Checked) ControlPaint.DrawMenuGlyph(e.Graphics,check,MenuGlyph.Checkmark,Theme.Background,Theme.Accent);
            int width=Math.Max(20,Width-textX-D(14));
            int height=TextRenderer.MeasureText(Text,Font,new Size(width,10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
            int top=D(Compact ? 8 : 15);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(textX,top,width,height+2),Theme.Text,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(!Compact) using(var f=new Font(Font.FontFamily,Font.Size-0.5f)) TextRenderer.DrawText(e.Graphics,Description,f,new Rectangle(textX,top+height+D(8),width,Math.Max(1,Height-top-height-D(14))),Theme.Muted,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-5,-5),Theme.Accent,fill);
        }
    }
    public static class WindowTheme {
        static int captionResult;
        [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd,string app,string ids);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int length);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out int value,int length);
        public static void Apply(IntPtr hwnd) {
            try { int dark=1; DwmSetWindowAttribute(hwnd,20,ref dark,4); int bg=ColorTranslator.ToWin32(Theme.Background), fg=ColorTranslator.ToWin32(Theme.Text); captionResult=DwmSetWindowAttribute(hwnd,35,ref bg,4); DwmSetWindowAttribute(hwnd,36,ref fg,4); } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { }
        }
        public static void Scrollbar(IntPtr hwnd) { try { SetWindowTheme(hwnd,"DarkMode_Explorer",null); } catch(DllNotFoundException) { } catch(EntryPointNotFoundException) { } }
        public static string Inspect(IntPtr hwnd) { int dark; int a=DwmGetWindowAttribute(hwnd,20,out dark,4); return "DWM dark: " + a + "/" + dark + "; caption set HRESULT: " + captionResult; }
    }
}
