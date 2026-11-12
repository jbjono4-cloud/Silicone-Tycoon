using System;
using Godot;

namespace SiliconeTycoon.UI
{
    /// <summary>Shared colours, theme and small widget helpers. (Adapted from Console Tycoon's Ui; teal accent instead of copper.)</summary>
    public static class Ui
    {
        public static readonly Color Bg = Color.FromHtml("#0f1316");
        public static readonly Color Panel = Color.FromHtml("#151b20");
        public static readonly Color Panel2 = Color.FromHtml("#1b232a");
        public static readonly Color Line = Color.FromHtml("#26313a");
        public static readonly Color Fg = Color.FromHtml("#e4e9ec");
        public static readonly Color Muted = Color.FromHtml("#8a98a3");
        public static readonly Color Accent = Color.FromHtml("#5cc8d6");
        public static readonly Color Ok = Color.FromHtml("#62c17d");
        public static readonly Color Warn = Color.FromHtml("#e3b448");
        public static readonly Color Bad = Color.FromHtml("#e85a48");
        public static readonly Color Rival1 = Color.FromHtml("#c88cf0");
        public static readonly Color Rival2 = Color.FromHtml("#f0a35c");

        /// <summary>News severity colour: 0 info, 1 good, 2 warning, 3 bad.</summary>
        public static Color Sev(int sev) => sev >= 3 ? Bad : sev == 2 ? Warn : sev == 1 ? Ok : Fg;
        /// <summary>Design note colour: 0 info, 1 warning, 2 error.</summary>
        public static Color NoteSev(int sev) => sev >= 2 ? Bad : sev == 1 ? Warn : Muted;

        static StyleBoxFlat Box(Color bg, Color border, int radius = 6, int pad = 10, int borderW = 1)
        {
            var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
            sb.SetBorderWidthAll(borderW);
            sb.SetCornerRadiusAll(radius);
            sb.SetContentMarginAll(pad);
            return sb;
        }

        public static Theme MakeTheme()
        {
            var t = new Theme();
            t.DefaultFontSize = 15;
            t.SetColor("font_color", "Label", Fg);
            foreach (var type in new[] { "Button", "OptionButton", "CheckBox", "MenuButton" })
            {
                t.SetColor("font_color", type, Fg);
                t.SetColor("font_hover_color", type, Fg);
                t.SetColor("font_pressed_color", type, Accent);
                t.SetColor("font_disabled_color", type, Muted);
                t.SetColor("font_focus_color", type, Fg);
            }
            var normal = Box(Panel2, Line, 5, 6);
            normal.ContentMarginLeft = 10; normal.ContentMarginRight = 10;
            var hover = Box(Panel2, Accent, 5, 6);
            hover.ContentMarginLeft = 10; hover.ContentMarginRight = 10;
            var pressed = Box(Color.FromHtml("#16282c"), Accent, 5, 6);
            pressed.ContentMarginLeft = 10; pressed.ContentMarginRight = 10;
            var disabled = Box(Panel, Line, 5, 6);
            disabled.ContentMarginLeft = 10; disabled.ContentMarginRight = 10;
            foreach (var type in new[] { "Button", "OptionButton" })
            {
                t.SetStylebox("normal", type, normal);
                t.SetStylebox("hover", type, hover);
                t.SetStylebox("pressed", type, pressed);
                t.SetStylebox("disabled", type, disabled);
                t.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), Accent, 5, 6));
            }
            t.SetStylebox("panel", "PanelContainer", Box(Panel, Line, 8, 12));
            t.SetStylebox("panel", "TabContainer", Box(Bg, Bg, 0, 12, 0));
            t.SetStylebox("tab_selected", "TabContainer", Box(Panel, Accent, 5, 8));
            t.SetStylebox("tab_unselected", "TabContainer", Box(Bg, Line, 5, 8));
            t.SetStylebox("tab_hovered", "TabContainer", Box(Panel, Line, 5, 8));
            t.SetColor("font_selected_color", "TabContainer", Accent);
            t.SetColor("font_unselected_color", "TabContainer", Muted);
            t.SetColor("font_hovered_color", "TabContainer", Fg);
            t.SetStylebox("normal", "LineEdit", Box(Panel2, Line, 5, 6));
            t.SetStylebox("focus", "LineEdit", Box(Panel2, Accent, 5, 6));
            t.SetColor("font_color", "LineEdit", Fg);
            t.SetStylebox("background", "ProgressBar", Box(Line, Line, 3, 0, 0));
            t.SetStylebox("fill", "ProgressBar", Box(Accent, Accent, 3, 0, 0));
            t.SetColor("default_color", "RichTextLabel", Fg);
            t.SetStylebox("panel", "PopupMenu", Box(Panel2, Line, 5, 6));
            t.SetColor("font_color", "PopupMenu", Fg);
            t.SetColor("font_hover_color", "PopupMenu", Accent);
            t.SetStylebox("panel", "TooltipPanel", Box(Panel2, Line, 4, 6));
            t.SetColor("font_color", "TooltipLabel", Fg);
            return t;
        }

        public static Label L(string text, int size = 15, Color? color = null)
        {
            var l = new Label { Text = text };
            if (size != 15) l.AddThemeFontSizeOverride("font_size", size);
            if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
            return l;
        }

        public static Label Wrap(string text, int size = 14, Color? color = null)
        {
            var l = L(text, size, color);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.CustomMinimumSize = new Vector2(120, 0);
            return l;
        }

        public static Label Heading(string text) => L(text.ToUpperInvariant(), 12, Muted);

        public static Label Title(string text, int size = 18) => L(text, size, Fg);

        public static Button Btn(string text, Action onPress, string tooltip = null)
        {
            var b = new Button { Text = text };
            if (tooltip != null) b.TooltipText = tooltip;
            b.Pressed += onPress;
            return b;
        }

        public static Button Primary(string text, Action onPress)
        {
            var b = Btn(text, onPress);
            var dark = Color.FromHtml("#0b1416");
            var sb = Box(Accent, Accent, 5, 6);
            sb.ContentMarginLeft = 12; sb.ContentMarginRight = 12;
            var sbh = Box(Color.FromHtml("#7ad6e2"), Accent, 5, 6);
            sbh.ContentMarginLeft = 12; sbh.ContentMarginRight = 12;
            b.AddThemeStyleboxOverride("normal", sb);
            b.AddThemeStyleboxOverride("hover", sbh);
            b.AddThemeStyleboxOverride("pressed", sbh);
            b.AddThemeColorOverride("font_color", dark);
            b.AddThemeColorOverride("font_hover_color", dark);
            b.AddThemeColorOverride("font_pressed_color", dark);
            return b;
        }

        public static VBoxContainer V(int sep = 8)
        {
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", sep);
            return v;
        }

        public static HBoxContainer H(int sep = 8)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", sep);
            return h;
        }

        public static PanelContainer Card(Control content)
        {
            var p = new PanelContainer();
            p.AddChild(content);
            return p;
        }

        public static (PanelContainer card, VBoxContainer body) CardV(string heading, int sep = 8)
        {
            var body = V(sep);
            if (heading != null) body.AddChild(Heading(heading));
            return (Card(body), body);
        }

        public static ScrollContainer Scroll(Control content)
        {
            var sc = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            sc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            sc.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            sc.AddChild(content);
            return sc;
        }

        public static Control Spacer()
        {
            var c = new Control();
            c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            return c;
        }

        public static ProgressBar Bar(double value, double max = 1, Color? fill = null, float width = 120)
        {
            var b = new ProgressBar { MinValue = 0, MaxValue = Math.Max(max, 1e-9), Value = value, ShowPercentage = false };
            b.CustomMinimumSize = new Vector2(width, 8);
            b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            if (fill.HasValue)
            {
                var sb = Box(fill.Value, fill.Value, 3, 0, 0);
                b.AddThemeStyleboxOverride("fill", sb);
            }
            return b;
        }

        public static OptionButton Options(string[] items, int selected, Action<int> onSelect)
        {
            var o = new OptionButton();
            for (int i = 0; i < items.Length; i++) o.AddItem(items[i], i);
            if (items.Length > 0) o.Selected = Math.Clamp(selected, 0, items.Length - 1);
            o.ItemSelected += idx => onSelect((int)idx);
            return o;
        }

        public static SpinBox Spin(double min, double max, double step, double value, Action<double> onChange, string prefix = "", string suffix = "")
        {
            var s = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, Prefix = prefix, Suffix = suffix };
            s.CustomMinimumSize = new Vector2(130, 0);
            s.ValueChanged += v => onChange(v);
            return s;
        }

        /// <summary>A label and value on one line, value right-aligned.</summary>
        public static HBoxContainer Row(string label, string value, Color? valueColor = null, int size = 14)
        {
            var row = H(8);
            var l = L(label, size, Muted);
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(l);
            var v = L(value, size, valueColor ?? Fg);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(v);
            return row;
        }

        public static ColorRect Rule()
        {
            var r = new ColorRect { Color = Line, CustomMinimumSize = new Vector2(0, 1) };
            r.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            return r;
        }

        public static void Clear(Node n)
        {
            foreach (Node c in n.GetChildren())
            {
                n.RemoveChild(c);
                c.QueueFree();
            }
        }

        public static RichTextLabel Rich()
        {
            var r = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false };
            r.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            return r;
        }

        public static string Hex(Color c) => "#" + c.ToHtml(false);

        /// <summary>Escapes text for a BBCode label.</summary>
        public static string Esc(string s) => s.Replace("[", "[lb]");

        public static string Units(double v)
        {
            if (v >= 1e6) return (v / 1e6).ToString("0.0") + "M";
            if (v >= 1e4) return (v / 1e3).ToString("0") + "k";
            return v.ToString("N0");
        }
    }
}
