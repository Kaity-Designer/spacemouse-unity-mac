#if UNITY_EDITOR_OSX
using UnityEditor;
using UnityEngine;

namespace Kah.SpaceMouse
{
    public enum NavigationMode { Fly = 0, Orbit = 1 }

    // What the camera does, in the order the mapping table uses.
    public enum Channel { MoveRight = 0, MoveUp = 1, MoveForward = 2, LookUp = 3, TurnRight = 4, Roll = 5 }

    public enum ButtonAction { None = 0, FrameSelected = 1, ToggleMode = 2, ToggleRoll = 3, ResetSpeed = 4, OriginalSettings = 5 }

    // Per-user settings, stored in EditorPrefs so they follow you across projects.
    public static class SpaceMouseSettings
    {
        const string P = "Kah.SpaceMouse.";

        public static readonly string[] ChannelLabels =
            { "Move right", "Move up", "Move forward", "Look up", "Turn right", "Roll" };
        public static readonly string[] AxisLabels =
            { "TX (slide sideways)", "TY (push / pull)", "TZ (lift / press)", "RX (tilt fwd / back)", "RY (tilt sideways)", "RZ (twist)" };

        // Defaults measured on a SpaceMouse Wireless with the macOS driver.
        static readonly int[] DefaultSource = { 0, 2, 1, 3, 5, 4 };
        static readonly bool[] DefaultInvert = { false, true, true, true, false, true };

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(P + "Enabled", true);
            set => EditorPrefs.SetBool(P + "Enabled", value);
        }

        public static NavigationMode Mode
        {
            get => (NavigationMode)EditorPrefs.GetInt(P + "Mode", (int)NavigationMode.Fly);
            set => EditorPrefs.SetInt(P + "Mode", (int)value);
        }

        public static float MoveSpeed
        {
            get => EditorPrefs.GetFloat(P + "MoveSpeed", 1f);
            set => EditorPrefs.SetFloat(P + "MoveSpeed", Mathf.Clamp(value, 0.05f, 10f));
        }

        public static float RotateSpeed
        {
            get => EditorPrefs.GetFloat(P + "RotateSpeed", 1f);
            set => EditorPrefs.SetFloat(P + "RotateSpeed", Mathf.Clamp(value, 0.05f, 10f));
        }

        public static float ZoomSpeed
        {
            get => EditorPrefs.GetFloat(P + "ZoomSpeed", 1f);
            set => EditorPrefs.SetFloat(P + "ZoomSpeed", Mathf.Clamp(value, 0.05f, 10f));
        }

        // Raw value that counts as full deflection.
        public static float FullScale
        {
            get => EditorPrefs.GetFloat(P + "FullScale", 350f);
            set => EditorPrefs.SetFloat(P + "FullScale", Mathf.Clamp(value, 50f, 2000f));
        }

        public static float DeadZone
        {
            get => EditorPrefs.GetFloat(P + "DeadZone", 0.06f);
            set => EditorPrefs.SetFloat(P + "DeadZone", Mathf.Clamp(value, 0f, 0.5f));
        }

        // 1 = linear, higher = finer control near the centre.
        public static float Curve
        {
            get => EditorPrefs.GetFloat(P + "Curve", 1.6f);
            set => EditorPrefs.SetFloat(P + "Curve", Mathf.Clamp(value, 1f, 3f));
        }

        public static bool LockHorizon
        {
            get => EditorPrefs.GetBool(P + "LockHorizon", true);
            set => EditorPrefs.SetBool(P + "LockHorizon", value);
        }

        public static bool DominantAxis
        {
            get => EditorPrefs.GetBool(P + "DominantAxis", false);
            set => EditorPrefs.SetBool(P + "DominantAxis", value);
        }

        public static bool ChannelEnabled(Channel c) => EditorPrefs.GetBool(P + "On" + (int)c, true);
        public static void SetChannelEnabled(Channel c, bool v) => EditorPrefs.SetBool(P + "On" + (int)c, v);

        public static int Source(Channel c) => EditorPrefs.GetInt(P + "Src" + (int)c, DefaultSource[(int)c]);
        public static void SetSource(Channel c, int axis) => EditorPrefs.SetInt(P + "Src" + (int)c, Mathf.Clamp(axis, 0, 5));

        public static bool Invert(Channel c) => EditorPrefs.GetBool(P + "Inv" + (int)c, DefaultInvert[(int)c]);
        public static void SetInvert(Channel c, bool v) => EditorPrefs.SetBool(P + "Inv" + (int)c, v);

        public static ButtonAction Button(int index)
        {
            var def = index == 0 ? ButtonAction.FrameSelected : index == 1 ? ButtonAction.ToggleMode : ButtonAction.None;
            return (ButtonAction)EditorPrefs.GetInt(P + "Btn" + index, (int)def);
        }
        public static void SetButton(int index, ButtonAction a) => EditorPrefs.SetInt(P + "Btn" + index, (int)a);

        public static void ResetMapping()
        {
            for (int i = 0; i < 6; i++)
            {
                EditorPrefs.DeleteKey(P + "Src" + i);
                EditorPrefs.DeleteKey(P + "Inv" + i);
                EditorPrefs.DeleteKey(P + "On" + i);
            }
        }

        public static void ResetAll()
        {
            ResetMapping();
            foreach (var k in new[] { "Enabled", "Mode", "MoveSpeed", "RotateSpeed", "ZoomSpeed", "FullScale",
                                      "DeadZone", "Curve", "LockHorizon", "DominantAxis", "Btn0", "Btn1" })
                EditorPrefs.DeleteKey(P + k);
        }
    }
}
#endif
