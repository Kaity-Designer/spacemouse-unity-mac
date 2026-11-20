#if UNITY_EDITOR_OSX
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kah.SpaceMouse
{
    // Window > SpaceMouse: live monitor, speeds and axis mapping in one place.
    public class SpaceMouseWindow : EditorWindow
    {
        Vector2 m_Scroll;
        bool m_ShowMapping = true;
        bool m_ShowDebug;

        [MenuItem("Window/SpaceMouse")]
        public static void Open()
        {
            var w = GetWindow<SpaceMouseWindow>("SpaceMouse");
            w.minSize = new Vector2(360, 420);
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            SpaceMouseGUI.Draw(ref m_ShowMapping, ref m_ShowDebug);
            EditorGUILayout.EndScrollView();
        }
    }

    static class SpaceMouseSettingsProvider
    {
        static bool s_ShowMapping = true, s_ShowDebug;

        [SettingsProvider]
        static SettingsProvider Create() =>
            new SettingsProvider("Preferences/SpaceMouse", SettingsScope.User)
            {
                label = "SpaceMouse",
                guiHandler = _ => SpaceMouseGUI.Draw(ref s_ShowMapping, ref s_ShowDebug),
                keywords = new HashSet<string> { "SpaceMouse", "3Dconnexion", "3D mouse", "navigation" },
            };
    }

    static class SpaceMouseGUI
    {
        public static void Draw(ref bool showMapping, ref bool showDebug)
        {
            DrawStatus();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Navigation", EditorStyles.boldLabel);
            SpaceMouseSettings.Enabled = EditorGUILayout.Toggle("Enabled", SpaceMouseSettings.Enabled);
            SpaceMouseSettings.Mode = (NavigationMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mode", "Fly: you are the camera.\nOrbit: you hold the scene and turn it around its pivot."),
                SpaceMouseSettings.Mode);
            SpaceMouseSettings.MoveSpeed = EditorGUILayout.Slider("Move speed", SpaceMouseSettings.MoveSpeed, 0.05f, 10f);
            SpaceMouseSettings.RotateSpeed = EditorGUILayout.Slider("Rotate speed", SpaceMouseSettings.RotateSpeed, 0.05f, 10f);
            SpaceMouseSettings.ZoomSpeed = EditorGUILayout.Slider(new GUIContent("Zoom speed", "Orbit and 2D mode"), SpaceMouseSettings.ZoomSpeed, 0.05f, 10f);
            SpaceMouseSettings.LockHorizon = EditorGUILayout.Toggle(new GUIContent("Lock horizon", "Keeps the camera upright and ignores roll"), SpaceMouseSettings.LockHorizon);
            SpaceMouseSettings.DominantAxis = EditorGUILayout.Toggle(new GUIContent("Dominant axis only", "Only the strongest movement counts, handy for precise pans"), SpaceMouseSettings.DominantAxis);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Feel", EditorStyles.boldLabel);
            SpaceMouseSettings.DeadZone = EditorGUILayout.Slider(new GUIContent("Dead zone", "Ignore tiny movements around the centre"), SpaceMouseSettings.DeadZone, 0f, 0.5f);
            SpaceMouseSettings.Curve = EditorGUILayout.Slider(new GUIContent("Response curve", "1 = linear, higher = finer control for light touches"), SpaceMouseSettings.Curve, 1f, 3f);
            SpaceMouseSettings.FullScale = EditorGUILayout.Slider(new GUIContent("Full deflection", "Raw value that counts as a full push"), SpaceMouseSettings.FullScale, 50f, 2000f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Buttons", EditorStyles.boldLabel);
            for (int b = 0; b < 2; b++)
                SpaceMouseSettings.SetButton(b, (ButtonAction)EditorGUILayout.EnumPopup(
                    b == 0 ? "Left button" : "Right button", SpaceMouseSettings.Button(b)));

            EditorGUILayout.Space();
            showMapping = EditorGUILayout.Foldout(showMapping, "Axis mapping (fix a wrong direction here)", true);
            if (showMapping) DrawMapping();

            showDebug = EditorGUILayout.Foldout(showDebug, "Raw device input", true);
            if (showDebug) DrawRaw();

            EditorGUILayout.Space();
            if (GUILayout.Button(new GUIContent("Recenter Scene view",
                    "Back to Unity's starting view, looking at the world origin"), GUILayout.Height(28)))
                EditorApplication.ExecuteMenuItem("Tools/SpaceMouse/Recenter Scene View");
            if (GUILayout.Button(new GUIContent("Back to original settings",
                    "Puts every SpaceMouse setting back to how it was when first installed"), GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("Back to original settings?",
                        "This resets mode, speeds, feel, buttons and axis mapping to the defaults.",
                        "Reset", "Cancel"))
                    SpaceMouseSettings.ResetAll();
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Reset axis mapping only", EditorStyles.miniButton))
                SpaceMouseSettings.ResetMapping();
        }

        static void DrawStatus()
        {
            string msg;
            MessageType type;
            if (SpaceMouseNavigator.LibraryMissing)
            {
                msg = "SpaceMouseBridge.bundle could not be loaded. Re-import the package.";
                type = MessageType.Error;
            }
            else if (!SpaceMouseNavigator.Connected)
            {
                msg = SpaceMouseNative.Describe(SpaceMouseNavigator.StartResult);
                type = MessageType.Warning;
            }
            else
            {
                int n = SpaceMouseNative.SMB_DeviceCount();
                msg = "Connected to the 3Dconnexion driver" + (n > 0 ? "" : " (no device reported yet)")
                      + ".\nMove the cap with the mouse over a Scene view.";
                type = MessageType.Info;
            }
            EditorGUILayout.HelpBox(msg, type);
            if (!SpaceMouseNavigator.Connected && GUILayout.Button("Try again"))
                SpaceMouseNavigator.Connect();
        }

        static void DrawMapping()
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Each camera movement reads one puck axis. If something goes the wrong way, tick Invert. Watch the bars while you move the cap.", MessageType.None);
            for (int c = 0; c < 6; c++)
            {
                var ch = (Channel)c;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool on = EditorGUILayout.ToggleLeft(SpaceMouseSettings.ChannelLabels[c], SpaceMouseSettings.ChannelEnabled(ch), GUILayout.Width(120));
                    SpaceMouseSettings.SetChannelEnabled(ch, on);
                    using (new EditorGUI.DisabledScope(!on))
                    {
                        int src = EditorGUILayout.Popup(SpaceMouseSettings.Source(ch), SpaceMouseSettings.AxisLabels, GUILayout.MinWidth(80));
                        SpaceMouseSettings.SetSource(ch, src);
                        bool inv = GUILayout.Toggle(SpaceMouseSettings.Invert(ch), "Invert", EditorStyles.miniButton, GUILayout.Width(52));
                        SpaceMouseSettings.SetInvert(ch, inv);
                    }
                }
                Bar(SpaceMouseNavigator.Channels[c]);
            }
            EditorGUI.indentLevel--;
        }

        static void DrawRaw()
        {
            EditorGUI.indentLevel++;
            string[] names = { "TX", "TY", "TZ", "RX", "RY", "RZ" };
            float full = SpaceMouseSettings.FullScale;
            for (int i = 0; i < 6; i++)
            {
                int v = SpaceMouseNavigator.RawAxes[i];
                EditorGUILayout.LabelField(names[i], v.ToString());
                Bar(Mathf.Clamp(v / full, -1f, 1f));
            }
            EditorGUILayout.LabelField("Buttons", "0x" + SpaceMouseNavigator.RawButtons.ToString("X8"));
            if (SpaceMouseNavigator.Connected)
            {
                SpaceMouseNative.SMB_GetDebug(out uint cb, out uint other, out uint type, out uint lastClient, out uint cmd, out uint ours);
                EditorGUILayout.LabelField("Driver messages", cb.ToString());
                EditorGUILayout.LabelField("For other clients", other.ToString());
                EditorGUILayout.LabelField("Client id (ours / last)", ours + " / " + lastClient);
                EditorGUILayout.LabelField("Device active", SpaceMouseNative.SMB_IsActive() != 0 ? "yes (Unity in front)" : "no");
            }
            EditorGUI.indentLevel--;
        }

        static void Bar(float v)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 6f);
            r = EditorGUI.IndentedRect(r);
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.25f));
            float mid = r.x + r.width * 0.5f;
            float w = r.width * 0.5f * Mathf.Clamp(v, -1f, 1f);
            var fill = new Rect(w >= 0 ? mid : mid + w, r.y, Mathf.Abs(w), r.height);
            EditorGUI.DrawRect(fill, new Color(0.26f, 0.6f, 0.98f, 1f));
            EditorGUI.DrawRect(new Rect(mid - 0.5f, r.y, 1f, r.height), new Color(1f, 1f, 1f, 0.5f));
        }
    }
}
#endif
