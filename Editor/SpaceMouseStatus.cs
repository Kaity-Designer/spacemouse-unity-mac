#if UNITY_EDITOR_OSX
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kah.SpaceMouse
{
    public enum StatusLevel { Ready, Warning, Error }

    // One place that decides the green / amber / red light.
    public static class SpaceMouseStatus
    {
        public static readonly Color Green = new Color(0.30f, 0.80f, 0.35f);
        public static readonly Color Amber = new Color(0.95f, 0.70f, 0.20f);
        public static readonly Color Red = new Color(0.90f, 0.30f, 0.28f);

        public static StatusLevel Level(out string label)
        {
            if (SpaceMouseNavigator.LibraryMissing) { label = "Plugin not loaded"; return StatusLevel.Error; }
            if (!SpaceMouseNavigator.Connected) { label = "No 3Dconnexion driver"; return StatusLevel.Error; }
            if (SpaceMouseNative.SMB_DeviceCount() <= 0) { label = "No SpaceMouse found"; return StatusLevel.Warning; }
            if (!SpaceMouseSettings.Enabled) { label = "Navigation off"; return StatusLevel.Warning; }
            label = "SpaceMouse connected";
            return StatusLevel.Ready;
        }

        public static Color ColorOf(StatusLevel l) =>
            l == StatusLevel.Ready ? Green : l == StatusLevel.Warning ? Amber : Red;
    }

    // Small badge in the Scene view: a coloured light and a word or two.
    // Show or hide it from the Scene view's Overlays menu (the ⋮ or the ` key).
    [Overlay(typeof(SceneView), "kah-spacemouse-status", "SpaceMouse", true)]
    public class SpaceMouseStatusOverlay : Overlay
    {
        VisualElement m_Dot;
        Label m_Label;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 6;
            root.style.paddingTop = 2;
            root.style.paddingBottom = 2;

            m_Dot = new VisualElement();
            m_Dot.style.width = 10;
            m_Dot.style.height = 10;
            m_Dot.style.marginRight = 6;
            m_Dot.style.borderTopLeftRadius = 5;
            m_Dot.style.borderTopRightRadius = 5;
            m_Dot.style.borderBottomLeftRadius = 5;
            m_Dot.style.borderBottomRightRadius = 5;
            root.Add(m_Dot);

            m_Label = new Label();
            root.Add(m_Label);

            Refresh();
            root.schedule.Execute(Refresh).Every(500);
            return root;
        }

        void Refresh()
        {
            if (m_Dot == null) return;
            var level = SpaceMouseStatus.Level(out string label);
            m_Dot.style.backgroundColor = SpaceMouseStatus.ColorOf(level);
            m_Label.text = label;
            m_Label.tooltip = level == StatusLevel.Ready
                ? "Move the cap with the mouse over the Scene view."
                : "Open Window > SpaceMouse for details.";
        }
    }
}
#endif
