// Realistic Life Simulator - mouse-driven menu drawn with the game's own text renderer.
// Works without any keyboard shortcut: left click selects, right click / Esc goes back,
// the mouse wheel scrolls. Arrow keys + Enter also work (GTA's own frontend controls).
// Layout uses SHVDN's 1280x720 virtual screen, so it scales to any resolution.

using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using GTA.UI;
using RealisticLifeSimulator.Core;
using Font = GTA.UI.Font;
using Keys = System.Windows.Forms.Keys;

namespace RealisticLifeSimulator.Game
{
    // Direct native control calls (control group 0 = player). Independent of which
    // SHVDN v3 build is installed, since its managed control helpers changed names.
    public static class NativeInput
    {
        public static bool JustPressed(GTA.Control c)
        {
            return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, (int)c);
        }

        public static bool DisabledJustPressed(GTA.Control c)
        {
            return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, (int)c);
        }

        public static float DisabledNormal(GTA.Control c)
        {
            return Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, (int)c);
        }

        public static void Disable(GTA.Control c)
        {
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)c, true);
        }

        public static void DisableAll()
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
        }
    }

    public sealed class MenuItem
    {
        public string Label;
        public Func<string> Value;       // right-hand text, evaluated while drawn
        public Action OnSelect;           // null = information row
        public Func<bool> IsEnabled;      // null = always enabled
        public string Description;

        public MenuItem(string label, Action onSelect)
        {
            Label = label;
            OnSelect = onSelect;
        }

        public MenuItem WithValue(Func<string> value) { Value = value; return this; }
        public MenuItem WithDescription(string text) { Description = text; return this; }
        public MenuItem WithEnabled(Func<bool> enabled) { IsEnabled = enabled; return this; }

        public bool Enabled { get { return IsEnabled == null || IsEnabled(); } }
    }

    public sealed class Menu
    {
        public string Title;
        public string Subtitle;
        public readonly List<MenuItem> Items = new List<MenuItem>();
        public int Selected;
        public int Scroll;

        public Menu(string title, string subtitle)
        {
            Title = title;
            Subtitle = subtitle;
        }

        public MenuItem Add(MenuItem item) { Items.Add(item); return item; }
    }

    public sealed class MenuController
    {
        // Layout (virtual 1280x720 units)
        private const float PanelX = 60f, PanelY = 90f, PanelW = 440f;
        private const float HeaderH = 64f, RowH = 32f, DescH = 54f, FooterH = 26f;
        private const int MaxRows = 10;

        // Palette: dark neutral panel, one muted blue accent.
        private static readonly Color HeaderColor = Color.FromArgb(242, 22, 24, 29);
        private static readonly Color RowColor = Color.FromArgb(225, 30, 33, 39);
        private static readonly Color HoverColor = Color.FromArgb(255, 47, 52, 61);
        private static readonly Color Accent = Color.FromArgb(255, 92, 148, 214);
        private static readonly Color TextMain = Color.FromArgb(255, 236, 238, 241);
        private static readonly Color TextDim = Color.FromArgb(255, 150, 156, 166);
        private static readonly Color TextOff = Color.FromArgb(255, 95, 100, 108);
        private static readonly Color ValueColor = Color.FromArgb(255, 170, 196, 228);

        private readonly Stack<Menu> stack = new Stack<Menu>();
        private Action<Keys> captureCallback;
        private string captureMessage;
        private int openedAt;
        private int graceFrames;
        private float lastCursorX = -1f, lastCursorY = -1f;

        // Pre-allocated draw elements (no per-frame allocations for the panel itself).
        private readonly ContainerElement header, accentLine, rowBg, hoverBg, hoverBar, descBg, footerBg, scrollTrack, scrollThumb;
        private readonly TextElement title, subtitle, description, footer, pageInfo;
        private readonly TextElement[] labels = new TextElement[MaxRows];
        private readonly TextElement[] values = new TextElement[MaxRows];

        public MenuController()
        {
            header = new ContainerElement(PointF.Empty, new SizeF(PanelW, HeaderH), HeaderColor);
            accentLine = new ContainerElement(PointF.Empty, new SizeF(PanelW, 3f), Accent);
            rowBg = new ContainerElement(PointF.Empty, new SizeF(PanelW, RowH), RowColor);
            hoverBg = new ContainerElement(PointF.Empty, new SizeF(PanelW, RowH), HoverColor);
            hoverBar = new ContainerElement(PointF.Empty, new SizeF(3f, RowH), Accent);
            descBg = new ContainerElement(PointF.Empty, new SizeF(PanelW, DescH), HeaderColor);
            footerBg = new ContainerElement(PointF.Empty, new SizeF(PanelW, FooterH), Color.FromArgb(242, 17, 19, 23));
            scrollTrack = new ContainerElement(PointF.Empty, new SizeF(3f, 0f), Color.FromArgb(80, 255, 255, 255));
            scrollThumb = new ContainerElement(PointF.Empty, new SizeF(3f, 0f), Accent);

            title = new TextElement("", PointF.Empty, 0.58f, TextMain, Font.ChaletLondon, Alignment.Left, false, false);
            subtitle = new TextElement("", PointF.Empty, 0.30f, TextDim, Font.ChaletLondon, Alignment.Left, false, false);
            description = new TextElement("", PointF.Empty, 0.30f, TextDim, Font.ChaletLondon, Alignment.Left, false, false);
            description.WrapWidth = PanelW - 36f;
            footer = new TextElement("", PointF.Empty, 0.26f, TextOff, Font.ChaletLondon, Alignment.Left, false, false);
            pageInfo = new TextElement("", PointF.Empty, 0.26f, TextOff, Font.ChaletLondon, Alignment.Right, false, false);
            for (int i = 0; i < MaxRows; i++)
            {
                labels[i] = new TextElement("", PointF.Empty, 0.34f, TextMain, Font.ChaletLondon, Alignment.Left, false, false);
                values[i] = new TextElement("", PointF.Empty, 0.34f, ValueColor, Font.ChaletLondon, Alignment.Right, false, false);
            }
        }

        public bool IsOpen { get { return stack.Count > 0; } }
        public bool IsCapturingKey { get { return captureCallback != null; } }
        public Menu Current { get { return stack.Count > 0 ? stack.Peek() : null; } }

        public void Open(Menu root)
        {
            stack.Clear();
            captureCallback = null;
            root.Selected = 0;
            root.Scroll = 0;
            stack.Push(root);
            openedAt = Environment.TickCount;
        }

        public void Push(Menu menu)
        {
            menu.Selected = 0;
            menu.Scroll = 0;
            stack.Push(menu);
        }

        // Replace the top menu (used to refresh a page after a change).
        public void Replace(Menu menu)
        {
            int sel = stack.Count > 0 ? stack.Peek().Selected : 0;
            if (stack.Count > 0) stack.Pop();
            menu.Selected = Math.Min(sel, Math.Max(0, menu.Items.Count - 1));
            stack.Push(menu);
            EnsureVisible(menu);
        }

        public void Back()
        {
            if (stack.Count > 0) stack.Pop();
            if (stack.Count == 0) Close();
        }

        public void Close()
        {
            stack.Clear();
            captureCallback = null;
            graceFrames = 3; // keep Esc from opening the pause menu as the panel closes
        }

        public void BeginKeyCapture(string message, Action<Keys> onKey)
        {
            captureMessage = message;
            captureCallback = onKey;
        }

        public void CancelKeyCapture()
        {
            captureCallback = null;
        }

        // Called from the script's KeyDown event. Returns true when the menu consumed the key.
        public bool HandleKey(Keys key)
        {
            if (!IsOpen) return false;
            if (captureCallback != null)
            {
                Action<Keys> cb = captureCallback;
                captureCallback = null;
                openedAt = Environment.TickCount; // swallow the same press in HandleInput (Esc = back)
                if (key != Keys.Escape) cb(key);
                return true;
            }
            return true; // navigation is handled through game controls in OnFrame
        }

        public void OnFrame()
        {
            if (!IsOpen)
            {
                if (graceFrames > 0)
                {
                    graceFrames--;
                    NativeInput.Disable(GTA.Control.FrontendPause);
                    NativeInput.Disable(GTA.Control.FrontendPauseAlternate);
                }
                return;
            }

            Ped player = GTA.Game.Player.Character;
            if (GTA.Game.IsPaused || player == null || !player.Exists() || player.IsDead
                || Function.Call<bool>(Hash.IS_PAUSE_MENU_ACTIVE))
            {
                Close();
                return;
            }

            NativeInput.DisableAll();
            Function.Call((Hash)0xAAE7CE1D63167423UL); // SET_MOUSE_CURSOR_THIS_FRAME

            // Ignore input for a moment after opening so the opening press doesn't select a row.
            if (unchecked(Environment.TickCount - openedAt) > 150) HandleInput();
            if (IsOpen) Draw();
        }

        private void HandleInput()
        {
            Menu m = Current;
            float vw = GTA.UI.Screen.ScaledWidth;
            float cx = NativeInput.DisabledNormal(GTA.Control.CursorX) * vw;
            float cy = NativeInput.DisabledNormal(GTA.Control.CursorY) * 720f;

            bool back = NativeInput.DisabledJustPressed(GTA.Control.CursorCancel)
                     || NativeInput.DisabledJustPressed(GTA.Control.FrontendCancel);

            if (captureCallback != null)
            {
                if (NativeInput.DisabledJustPressed(GTA.Control.CursorCancel)) captureCallback = null;
                return; // keys arrive through HandleKey
            }

            if (back)
            {
                Back();
                return;
            }

            int visible = Math.Min(MaxRows, m.Items.Count);
            int hover = -1;
            float rowsTop = PanelY + HeaderH;
            if (cx >= PanelX && cx <= PanelX + PanelW && cy >= rowsTop && cy < rowsTop + visible * RowH)
                hover = m.Scroll + (int)((cy - rowsTop) / RowH);

            bool moved = Math.Abs(cx - lastCursorX) > 0.5f || Math.Abs(cy - lastCursorY) > 0.5f;
            lastCursorX = cx;
            lastCursorY = cy;
            if (hover >= 0 && hover < m.Items.Count && moved) m.Selected = hover;

            if (NativeInput.DisabledJustPressed(GTA.Control.CursorScrollUp)) ScrollBy(m, -1);
            if (NativeInput.DisabledJustPressed(GTA.Control.CursorScrollDown)) ScrollBy(m, 1);
            if (NativeInput.DisabledJustPressed(GTA.Control.FrontendUp)) MoveSelection(m, -1);
            if (NativeInput.DisabledJustPressed(GTA.Control.FrontendDown)) MoveSelection(m, 1);

            bool click = NativeInput.DisabledJustPressed(GTA.Control.CursorAccept);
            if (click)
            {
                if (hover >= 0 && hover < m.Items.Count) Activate(m, hover);
            }
            else if (NativeInput.DisabledJustPressed(GTA.Control.FrontendAccept))
            {
                Activate(m, m.Selected);
            }
        }

        private void Activate(Menu m, int index)
        {
            if (index < 0 || index >= m.Items.Count) return;
            MenuItem item = m.Items[index];
            m.Selected = index;
            if (item.OnSelect == null || !item.Enabled) return;
            Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET", true);
            try
            {
                item.OnSelect();
            }
            catch (Exception ex)
            {
                Log.Error("Menu action '" + item.Label + "' failed", ex);
            }
        }

        private void MoveSelection(Menu m, int delta)
        {
            if (m.Items.Count == 0) return;
            m.Selected = (m.Selected + delta + m.Items.Count) % m.Items.Count;
            EnsureVisible(m);
        }

        private void ScrollBy(Menu m, int delta)
        {
            int maxScroll = Math.Max(0, m.Items.Count - MaxRows);
            m.Scroll = Math.Max(0, Math.Min(maxScroll, m.Scroll + delta));
        }

        private static void EnsureVisible(Menu m)
        {
            if (m.Selected < m.Scroll) m.Scroll = m.Selected;
            if (m.Selected >= m.Scroll + MaxRows) m.Scroll = m.Selected - MaxRows + 1;
        }

        private void Draw()
        {
            Menu m = Current;
            float x = PanelX, y = PanelY;

            header.Position = new PointF(x, y);
            header.Draw();
            accentLine.Position = new PointF(x, y);
            accentLine.Draw();
            title.Caption = m.Title;
            title.Position = new PointF(x + 18f, y + 9f);
            title.Draw();
            subtitle.Caption = m.Subtitle ?? "";
            subtitle.Position = new PointF(x + 18f, y + 38f);
            subtitle.Draw();

            int visible = Math.Min(MaxRows, m.Items.Count);
            float rowsTop = y + HeaderH;
            for (int i = 0; i < visible; i++)
            {
                int idx = m.Scroll + i;
                if (idx >= m.Items.Count) break;
                MenuItem item = m.Items[idx];
                float ry = rowsTop + i * RowH;

                ContainerElement bg = idx == m.Selected ? hoverBg : rowBg;
                bg.Position = new PointF(x, ry);
                bg.Draw();
                if (idx == m.Selected && item.OnSelect != null)
                {
                    hoverBar.Position = new PointF(x, ry);
                    hoverBar.Draw();
                }

                bool enabled = item.Enabled;
                TextElement label = labels[i];
                label.Caption = item.Label;
                label.Color = !enabled ? TextOff : (item.OnSelect == null ? TextDim : TextMain);
                label.Position = new PointF(x + 18f, ry + 6f);
                label.Draw();

                if (item.Value != null)
                {
                    TextElement value = values[i];
                    string v;
                    try { v = item.Value(); } catch (Exception) { v = "?"; }
                    value.Caption = v ?? "";
                    value.Color = enabled ? ValueColor : TextOff;
                    value.Position = new PointF(x + PanelW - 18f, ry + 6f);
                    value.Draw();
                }
            }

            float listBottom = rowsTop + visible * RowH;
            if (m.Items.Count > MaxRows)
            {
                float trackH = visible * RowH;
                scrollTrack.Position = new PointF(x + PanelW - 4f, rowsTop);
                scrollTrack.Size = new SizeF(3f, trackH);
                scrollTrack.Draw();
                float thumbH = trackH * MaxRows / m.Items.Count;
                float thumbY = rowsTop + (trackH - thumbH) * m.Scroll / Math.Max(1, m.Items.Count - MaxRows);
                scrollThumb.Position = new PointF(x + PanelW - 4f, thumbY);
                scrollThumb.Size = new SizeF(3f, thumbH);
                scrollThumb.Draw();
            }

            // Description / prompt area
            string desc = captureCallback != null
                ? captureMessage + L.S("   (Esc or right-click to cancel)", "   (Esc или ПКМ - отмена)")
                : (m.Selected >= 0 && m.Selected < m.Items.Count ? m.Items[m.Selected].Description : null);
            float footerY = listBottom;
            if (!string.IsNullOrEmpty(desc))
            {
                descBg.Position = new PointF(x, listBottom);
                descBg.Draw();
                description.Caption = desc;
                description.Color = captureCallback != null ? TextMain : TextDim;
                description.Position = new PointF(x + 18f, listBottom + 8f);
                description.Draw();
                footerY += DescH;
            }

            footerBg.Position = new PointF(x, footerY);
            footerBg.Draw();
            footer.Caption = L.S("Left click: select    Right click / Esc: back    Wheel: scroll", "ЛКМ: выбрать    ПКМ / Esc: назад    Колесо: прокрутка");
            footer.Position = new PointF(x + 18f, footerY + 5f);
            footer.Draw();
            if (m.Items.Count > 0)
            {
                pageInfo.Caption = (m.Selected + 1) + " / " + m.Items.Count;
                pageInfo.Position = new PointF(x + PanelW - 18f, footerY + 5f);
                pageInfo.Draw();
            }
        }
    }
}
