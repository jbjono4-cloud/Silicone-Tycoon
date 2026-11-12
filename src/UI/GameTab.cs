using Godot;
using SiliconeTycoon.Sim;

namespace SiliconeTycoon.UI
{
    /// <summary>
    /// Base for the main tabs. A tab builds its fixed layout once in <see cref="Build"/> and redraws its changing parts
    /// in <see cref="Refresh"/>, which <see cref="Main"/> calls when the tab is shown, after player actions, and at most
    /// once a game month while time runs.
    /// </summary>
    public partial class GameTab : HBoxContainer
    {
        protected Main main;
        protected GameState S => main.S;
        protected GameData D => main.D;
        bool built;

        public GameTab() { }

        protected GameTab(Main m, string name)
        {
            main = m;
            Name = name;
            AddThemeConstantOverride("separation", 12);
        }

        public override void _Ready()
        {
            if (built || main == null) return;
            built = true;
            Build();
            Refresh();
        }

        protected virtual void Build() { }

        public virtual void Refresh() { }

        /// <summary>Shows an error (if any) and refreshes everything after a player action.</summary>
        protected void Act(string error, string success = null)
        {
            if (error != null) main.Toast(error);
            else if (success != null) main.Toast(success);
            main.RefreshAll();
        }
    }
}
