using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>Root of the game: start menu, top bar, tabs, real-time clock, auto-pause and saving. (Structure from Console Tycoon's Main.)</summary>
    public partial class Main : Control
    {
        public GameState S;
        public GameData D;

        const string SavePath = "user://save.json";
        const string AutosavePath = "user://autosave.json";
        /// <summary>The final state of the last company that went under, kept for a post-mortem.</summary>
        const string GameOverPath = "user://gameover.json";
        const string SettingsPath = "user://settings.cfg";
        /// <summary>Game weeks per real second at each speed.</summary>
        static readonly double[] WeeksPerSecond = { 0, 1, 2, 4, 8 };
        static readonly string[] SpeedNames = { "Pause", "1×", "2×", "4×", "8×" };

        Control startScreen, gameScreen;
        TabContainer tabs;
        readonly List<GameTab> tabList = new List<GameTab>();
        Label dateL, cashL, repL, nameL;
        Button alertBtn;
        Button[] speedBtns;
        PanelContainer toast;
        Label toastL;
        double toastTime, acc;
        int speed, lastSpeed = 1, newsSeen, lastRefreshMonth = -1;

        // auto-pause settings (saved with the interface settings)
        public bool PauseOnRfq = true;
        public bool PauseOnChips = true;
        public bool PauseOnBadNews = true;
        public bool PauseOnNodes = true;
        public bool PauseOnDecisions = true;
        readonly HashSet<string> shownDecisions = new HashSet<string>();
        PanelContainer decisionPopup;

        public override void _Ready()
        {
            LoadSettings();
            GetWindow().SizeChanged += ApplyScale;
            ApplyScale();
            SetAnchorsPreset(LayoutPreset.FullRect);
            Theme = Ui.MakeTheme();
            var bg = new ColorRect { Color = Ui.Bg };
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            bg.MouseFilter = MouseFilterEnum.Ignore;
            AddChild(bg);

            toast = new PanelContainer { Visible = false };
            toastL = Ui.L("", 15);
            toastL.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            toastL.CustomMinimumSize = new Vector2(420, 0);
            toast.AddChild(toastL);
            toast.ZIndex = 10;

            try
            {
                D = LoadData();
            }
            catch (Exception ex)
            {
                var err = Ui.Wrap("Couldn't load the game data in res://data: " + ex.Message, 16, Ui.Bad);
                err.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(err);
                return;
            }
            ShowStart();
            AddChild(toast);
        }

        static string ReadText(string path)
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f == null) throw new InvalidOperationException($"missing {path}");
            return f.GetAsText();
        }

        static GameData LoadData() => GameData.FromJson(
            ReadText("res://data/nodes.json"), ReadText("res://data/locations.json"),
            ReadText("res://data/blocks.json"), ReadText("res://data/packages.json"),
            ReadText("res://data/segments.json"), ReadText("res://data/customers.json"), ReadText("res://data/rivals.json"),
            ReadText("res://data/events.json"));

        // ================= start menu =================
        void ShowStart()
        {
            speed = 0;
            S = null;
            gameScreen?.QueueFree();
            gameScreen = null;
            tabList.Clear();
            startScreen?.QueueFree();
            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            startScreen = center;
            var box = Ui.V(12);
            box.CustomMinimumSize = new Vector2(580, 0);
            center.AddChild(Ui.Card(box));

            box.AddChild(Ui.L("DESIGN · FABS · THE NODE RACE", 12, Ui.Accent));
            box.AddChild(Ui.Title("Silicone Tycoon", 34));
            box.AddChild(Ui.Wrap("Found a chip company in 1970. Design chips block by block, build fabs around the world, win design wins from customers and survive the race to the next process node.", 15, Ui.Muted));

            box.AddChild(Ui.Heading("Company name"));
            var nameEdit = new LineEdit { Text = "Silicone Semiconductor", PlaceholderText = "Company name" };
            box.AddChild(nameEdit);

            box.AddChild(Ui.Heading("Start"));
            var defaults = new NewGameOptions();
            box.AddChild(Ui.Wrap($"Campaign: January 1970, a newly founded company with {Format.Big(defaults.StartingCash)}, {defaults.StartingEngineers} design engineers and the 10 µm process. " +
                                 "Meridian Integrated and Kestrel Devices are already in the business.", 14));

            var seedRow = Ui.H(10);
            seedRow.AddChild(Ui.L("World seed", 13, Ui.Muted));
            var seedEdit = new LineEdit { Text = new Random().Next(1, 999999).ToString(), CustomMinimumSize = new Vector2(120, 0) };
            seedRow.AddChild(seedEdit);
            seedRow.AddChild(Ui.L("Same seed, same world.", 12, Ui.Muted));
            box.AddChild(seedRow);

            var scaleRow = Ui.H(10);
            scaleRow.AddChild(Ui.L("Interface size", 13, Ui.Muted));
            scaleRow.AddChild(ScalePicker());
            scaleRow.AddChild(Ui.L("Ctrl + / Ctrl − / Ctrl 0", 12, Ui.Muted));
            box.AddChild(scaleRow);

            var rates = new[] { (name: "Off", rate: 0.0), (name: "Calm", rate: 0.5), (name: "Normal", rate: 1.0), (name: "Turbulent", rate: 1.6) };
            int rateIdx = 2;
            var eventRow = Ui.H(10);
            eventRow.AddChild(Ui.L("World events", 13, Ui.Muted));
            eventRow.AddChild(Ui.Options(rates.Select(r => r.name).ToArray(), rateIdx, i => rateIdx = i));
            eventRow.AddChild(Ui.L("Earthquakes, fires, strikes, recessions, booms... (Off also skips history's oil shocks and recessions.)", 12, Ui.Muted));
            box.AddChild(eventRow);

            var row = Ui.H(10);
            row.AddChild(Ui.Primary("Found the company", () =>
            {
                ulong seed = ulong.TryParse(seedEdit.Text.Trim(), out var v) ? v : 1;
                StartGame(Simulation.NewGame(D, new NewGameOptions { CompanyName = nameEdit.Text, Seed = seed, EventRate = rates[rateIdx].rate }));
            }));
            if (FileAccess.FileExists(SavePath)) row.AddChild(Ui.Btn("Continue saved game", () => LoadFromFile(SavePath)));
            if (FileAccess.FileExists(AutosavePath)) row.AddChild(Ui.Btn("Continue autosave", () => LoadFromFile(AutosavePath)));
            box.AddChild(row);

            AddChild(startScreen);
            MoveChild(startScreen, 1);
        }

        // ================= game screen =================
        void StartGame(GameState s)
        {
            S = s;
            shownDecisions.Clear();
            foreach (var x in S.Decisions) shownDecisions.Add(x.Id);
            CloseDecision();
            newsSeen = S.News.Count;
            lastRefreshMonth = -1;
            startScreen?.QueueFree();
            startScreen = null;
            gameScreen?.QueueFree();
            tabList.Clear();

            var root = Ui.V(10);
            root.SetAnchorsPreset(LayoutPreset.FullRect);
            root.OffsetLeft = 14; root.OffsetTop = 10; root.OffsetRight = -14; root.OffsetBottom = -10;
            gameScreen = root;

            var top = Ui.H(14);
            nameL = Ui.Title(S.CompanyName, 18);
            dateL = Ui.L("", 18, Ui.Accent);
            cashL = Ui.L("", 16);
            repL = Ui.L("", 14, Ui.Muted);
            alertBtn = Ui.Btn("", () => ShowTab("Company"), "Things to look at, on the Company tab");
            top.AddChild(nameL); top.AddChild(dateL); top.AddChild(cashL); top.AddChild(repL); top.AddChild(alertBtn); top.AddChild(Ui.Spacer());
            speedBtns = new Button[WeeksPerSecond.Length];
            for (int i = 0; i < speedBtns.Length; i++)
            {
                int sp = i;
                speedBtns[i] = new Button
                {
                    Text = SpeedNames[i],
                    ToggleMode = true,
                    TooltipText = i == 0 ? "Space toggles pause" : $"{WeeksPerSecond[i]:0} game week{(WeeksPerSecond[i] == 1 ? "" : "s")} a second",
                };
                speedBtns[i].Pressed += () => SetSpeed(sp);
                top.AddChild(speedBtns[i]);
            }
            top.AddChild(Ui.Btn("Next week", () => { SetSpeed(0); Advance(); }));
            top.AddChild(ScalePicker());
            top.AddChild(Ui.Btn("Save", SaveToFile));
            top.AddChild(Ui.Btn("Menu", () => { SaveToFile(); ShowStart(); }));
            root.AddChild(top);

            tabs = new TabContainer();
            tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
            tabList.Add(new CompanyTab(this));
            tabList.Add(new DesignTab(this));
            tabList.Add(new FabsTab(this));
            tabList.Add(new ResearchTab(this));
            tabList.Add(new MarketTab(this));
            foreach (var t in tabList) tabs.AddChild(t);
            tabs.TabChanged += _ => RefreshCurrent(force: true);
            root.AddChild(tabs);

            AddChild(root);
            MoveChild(root, 1);
            SetSpeed(0);
            RefreshAll();
            Toast("Paused. Start a chip in Design, a fab in Fabs, and the next node in Research, then press Space.");
        }

        /// <summary>Pauses the clock (for screens that need the player's full attention).</summary>
        public void PauseGame() => SetSpeed(0);

        void SetSpeed(int sp)
        {
            if (sp > 0) lastSpeed = sp;
            speed = sp;
            acc = 0;
            if (speedBtns == null) return;
            for (int i = 0; i < speedBtns.Length; i++) speedBtns[i].SetPressedNoSignal(i == sp);
        }

        public override void _Process(double delta)
        {
            if (toast.Visible)
            {
                toastTime -= delta;
                if (toastTime <= 0) toast.Visible = false;
            }
            if (S == null || speed == 0 || S.GameOver) return;
            acc += delta * WeeksPerSecond[speed];
            int steps = 0;
            while (acc >= 1 && speed > 0 && steps < 8)
            {
                acc -= 1;
                steps++;
                Advance();
            }
        }

        public override void _Input(InputEvent e)
        {
            if (e is InputEventKey k && k.Pressed && !k.Echo && (k.CtrlPressed || k.MetaPressed))
            {
                if (k.Keycode == Key.Equal || k.Keycode == Key.Plus || k.Keycode == Key.KpAdd) { StepScale(1); GetViewport().SetInputAsHandled(); }
                else if (k.Keycode == Key.Minus || k.Keycode == Key.KpSubtract) { StepScale(-1); GetViewport().SetInputAsHandled(); }
                else if (k.Keycode == Key.Key0 || k.Keycode == Key.Kp0) { SetScale(true, uiScale); GetViewport().SetInputAsHandled(); }
            }
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (S == null) return;
            if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Space)
            {
                SetSpeed(speed == 0 ? lastSpeed : 0);
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>Runs one game week, then refreshes and pauses for anything that needs the player.</summary>
        void Advance()
        {
            if (S == null || S.GameOver) return;
            bool wasOver = S.GameOver;
            bool wasPositive = S.Cash >= 0;
            Simulation.Advance(D, S);

            string pauseFor = null;
            for (; newsSeen < S.News.Count; newsSeen++)
            {
                var n = S.News[newsSeen];
                if (pauseFor != null) continue;
                if (PauseOnRfq && n.Kind == NewsKinds.Rfq) pauseFor = n.Text + " (see Market)";
                else if (PauseOnChips && n.Kind == NewsKinds.Validated) pauseFor = n.Text + " (see Design)";
                else if (PauseOnNodes && n.Kind == NewsKinds.NodeReady) pauseFor = n.Text;
                else if (PauseOnBadNews && n.Severity >= 3) pauseFor = n.Text;
            }
            if (S.News.Count < newsSeen) newsSeen = S.News.Count;   // the log trims old items
            if (wasPositive && S.Cash < 0 && !S.GameOver)
                pauseFor = $"You're overdrawn. Borrow on the Company tab or cut costs within {Simulation.MaxOverdrawnMonths} months.";
            if (pauseFor != null)
            {
                SetSpeed(0);
                Toast("Paused: " + pauseFor);
            }
            var fresh = S.Decisions.FirstOrDefault(x => !shownDecisions.Contains(x.Id));
            if (fresh != null)
            {
                foreach (var x in S.Decisions) shownDecisions.Add(x.Id);
                if (PauseOnDecisions)
                {
                    SetSpeed(0);
                    ShowDecision(fresh);
                }
            }

            if (!S.GameOver && Calendar.IsQuarterEnd(S.Tick - 1)) WriteSave(AutosavePath, quiet: true);

            RefreshTop();
            int month = S.MonthIndex;
            if (speed == 0 || month != lastRefreshMonth) RefreshCurrent(force: false);

            if (S.GameOver && !wasOver)
            {
                SetSpeed(0);
                WriteSave(GameOverPath, quiet: true);
                var d = new AcceptDialog { Title = "The end", DialogText = S.EndText, OkButtonText = "Back to menu" };
                d.Confirmed += () => ShowStart();
                AddChild(d);
                d.PopupCentered(new Vector2I(520, 200));
            }
        }

        /// <summary>A pop-up for an event that needs a decision: the options, or decide later on the Company tab.</summary>
        void ShowDecision(Decision x)
        {
            decisionPopup?.QueueFree();
            var panel = new PanelContainer { ZIndex = 20 };
            var box = Ui.V(10);
            box.CustomMinimumSize = new Vector2(560, 0);
            panel.AddChild(box);
            box.AddChild(Ui.L("DECISION", 12, Ui.Warn));
            box.AddChild(Ui.Title(x.Title, 20));
            box.AddChild(Ui.Wrap(x.Text, 15));
            for (int i = 0; i < x.Options.Count; i++)
            {
                int pick = i;
                var row = Ui.H(10);
                var btn = Ui.Primary(x.Options[i].Label, () =>
                {
                    string err = Events.Choose(D, S, x.Id, pick);
                    if (err != null) { Toast(err); return; }
                    CloseDecision();
                    RefreshAll();
                });
                btn.CustomMinimumSize = new Vector2(190, 0);
                row.AddChild(btn);
                row.AddChild(Ui.Wrap(x.Options[i].Detail, 13, Ui.Muted));
                box.AddChild(row);
            }
            var later = Ui.H(10);
            later.AddChild(Ui.Btn("Decide later", () => { CloseDecision(); ShowTab("Company"); RefreshAll(); }));
            later.AddChild(Ui.L($"By {Calendar.WeekLabel(x.DeadlineTick)}, or \"{x.Options[x.Default].Label}\" happens.", 12, Ui.Muted));
            box.AddChild(later);
            decisionPopup = panel;
            AddChild(panel);
            panel.ResetSize();
            panel.Position = new Vector2((Size.X - panel.Size.X) / 2, Math.Max(40, (Size.Y - panel.Size.Y) / 3));
        }

        void CloseDecision()
        {
            decisionPopup?.QueueFree();
            decisionPopup = null;
        }

        void RefreshTop()
        {
            if (S == null || dateL == null) return;
            dateL.Text = Calendar.WeekLabel(S.Tick);
            cashL.Text = Format.Money(S.Cash) + (S.Ledger.Debt > 0 ? $"  (debt {Format.Big(S.Ledger.Debt)})" : "");
            cashL.AddThemeColorOverride("font_color", S.Cash < 0 ? Ui.Bad : Ui.Fg);
            repL.Text = $"Reputation {S.Rep * 100:+0;-0;0} ({GameState.RepWord(S.Rep)})";
            if (Calendar.WeekInMonth(S.Tick) == 0 || speed == 0)
            {
                var alerts = Advisor.Alerts(D, S);
                int serious = alerts.Count(a => a.Severity >= 2);
                alertBtn.Text = alerts.Count == 0 ? "All clear" : $"{alerts.Count} alert{(alerts.Count == 1 ? "" : "s")}";
                var col = serious > 0 && alerts.Any(a => a.Severity >= 3) ? Ui.Bad : serious > 0 ? Ui.Warn : Ui.Muted;
                alertBtn.AddThemeColorOverride("font_color", col);
                alertBtn.AddThemeColorOverride("font_hover_color", col);
            }
        }

        /// <summary>Refreshes the visible tab, unless the player is typing in it.</summary>
        void RefreshCurrent(bool force)
        {
            if (S == null || tabs == null) return;
            if (tabs.GetCurrentTabControl() is not GameTab tab) return;
            if (!force && GetViewport().GuiGetFocusOwner() is LineEdit edit && tab.IsAncestorOf(edit)) return;
            lastRefreshMonth = S.MonthIndex;
            tab.Refresh();
        }

        /// <summary>Refreshes the top bar and the visible tab. Call after any player action.</summary>
        public void RefreshAll()
        {
            RefreshTop();
            RefreshCurrent(force: true);
        }

        public void Toast(string text)
        {
            if (toast == null || toastL == null) return;
            toastL.Text = text;
            toast.Visible = true;
            toastTime = Math.Clamp(text.Length / 15.0, 4, 10);
            toast.ResetSize();
            toast.Position = new Vector2((Size.X - toast.Size.X) / 2, Size.Y - toast.Size.Y - 24);
        }

        /// <summary>Switches to a tab by its name.</summary>
        public void ShowTab(string name)
        {
            var t = tabList.FirstOrDefault(x => x.Name == name);
            if (t != null) tabs.CurrentTab = t.GetIndex();
        }

        // ================= UI scaling (from Console Tycoon) =================
        static readonly float[] Scales = { 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f };
        float uiScale = 1f;
        bool autoScale = true;
        readonly List<OptionButton> scalePickers = new List<OptionButton>();

        static float AutoScale()
        {
            int screen = DisplayServer.WindowGetCurrentScreen();
            float dpi = DisplayServer.ScreenGetScale(screen);
            Vector2I size = DisplayServer.ScreenGetSize(screen);
            float byRes = size.Y >= 2000 ? 1.75f : size.Y >= 1550 ? 1.5f : size.Y >= 1300 ? 1.25f : size.Y >= 1000 ? 1f : 0.9f;
            return Math.Clamp(Math.Max(byRes, dpi > 1.01f ? dpi : 0f), 0.75f, 2.5f);
        }

        float EffectiveScale => autoScale ? AutoScale() : uiScale;

        void ApplyScale()
        {
            var w = GetWindow();
            float s = EffectiveScale;
            w.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
            w.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
            var size = w.Size;
            w.ContentScaleSize = new Vector2I(Math.Max(320, (int)(size.X / s)), Math.Max(200, (int)(size.Y / s)));
        }

        void SetScale(bool auto, float scale)
        {
            autoScale = auto;
            uiScale = Math.Clamp(scale, Scales[0], Scales[Scales.Length - 1]);
            ApplyScale();
            SaveSettings();
            scalePickers.RemoveAll(o => !IsInstanceValid(o));
            foreach (var o in scalePickers) o.Selected = PickerIndex();
            Toast($"Interface size {(autoScale ? "auto, " : "")}{EffectiveScale * 100:0}%");
        }

        void StepScale(int dir)
        {
            float cur = EffectiveScale;
            int i = dir > 0 ? Array.FindIndex(Scales, v => v > cur + 0.01f) : Array.FindLastIndex(Scales, v => v < cur - 0.01f);
            if (i < 0) return;
            SetScale(false, Scales[i]);
        }

        int PickerIndex() => autoScale ? 0 : 1 + Math.Max(0, Array.FindIndex(Scales, v => Math.Abs(v - uiScale) < 0.01f));

        OptionButton ScalePicker()
        {
            var names = new[] { $"Auto ({AutoScale() * 100:0}%)" }.Concat(Scales.Select(v => $"{v * 100:0}%")).ToArray();
            var o = Ui.Options(names, PickerIndex(), i => { if (i == 0) SetScale(true, uiScale); else SetScale(false, Scales[i - 1]); });
            o.TooltipText = "Interface size (Ctrl + / Ctrl − / Ctrl 0 for auto)";
            scalePickers.Add(o);
            return o;
        }

        void LoadSettings()
        {
            var cf = new ConfigFile();
            if (cf.Load(SettingsPath) != Error.Ok) return;
            autoScale = cf.GetValue("ui", "auto", true).AsBool();
            uiScale = cf.GetValue("ui", "scale", 1.0f).AsSingle();
            PauseOnRfq = cf.GetValue("pause", "rfq", true).AsBool();
            PauseOnChips = cf.GetValue("pause", "chips", true).AsBool();
            PauseOnBadNews = cf.GetValue("pause", "bad", true).AsBool();
            PauseOnNodes = cf.GetValue("pause", "nodes", true).AsBool();
            PauseOnDecisions = cf.GetValue("pause", "decisions", true).AsBool();
        }

        public void SaveSettings()
        {
            var cf = new ConfigFile();
            cf.SetValue("ui", "auto", autoScale);
            cf.SetValue("ui", "scale", uiScale);
            cf.SetValue("pause", "rfq", PauseOnRfq);
            cf.SetValue("pause", "chips", PauseOnChips);
            cf.SetValue("pause", "bad", PauseOnBadNews);
            cf.SetValue("pause", "nodes", PauseOnNodes);
            cf.SetValue("pause", "decisions", PauseOnDecisions);
            cf.Save(SettingsPath);
        }

        // ================= saving =================
        void SaveToFile()
        {
            if (S == null || S.GameOver) return;
            WriteSave(SavePath, quiet: false);
        }

        void WriteSave(string path, bool quiet)
        {
            if (S == null) return;
            try
            {
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
                f.StoreString(SaveGame.Serialize(S));
                if (!quiet) Toast("Game saved.");
            }
            catch (Exception ex)
            {
                Toast("Couldn't save: " + ex.Message);
            }
        }

        void LoadFromFile(string path)
        {
            try
            {
                StartGame(SaveGame.Deserialize(ReadText(path)));
            }
            catch (Exception ex)
            {
                Toast("Couldn't load the save: " + ex.Message);
            }
        }
    }
}
