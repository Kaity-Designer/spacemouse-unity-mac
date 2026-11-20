#if UNITY_EDITOR_OSX
using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Kah.SpaceMouse
{
    // Polls the bridge every editor tick and drives the Scene view camera.
    [InitializeOnLoad]
    public static class SpaceMouseNavigator
    {
        const string ClientName = "Unity";

        public static int StartResult { get; private set; } = -1;
        public static readonly int[] RawAxes = new int[6];
        public static uint RawButtons { get; private set; }
        public static readonly float[] Channels = new float[6]; // after mapping, dead zone and curve, -1..1

        static double s_LastTime;
        static uint s_PrevButtons;
        static bool s_LoadFailed;

        static SpaceMouseNavigator()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.quitting -= Shutdown;
            EditorApplication.quitting += Shutdown;
            Connect();
        }

        public static bool Connected => StartResult == SpaceMouseNative.OK;
        public static bool LibraryMissing => s_LoadFailed;

        public static void Connect()
        {
            try
            {
                StartResult = SpaceMouseNative.SMB_Start(ClientName, SpaceMouseNative.UnityEditorSignature);
                s_LoadFailed = false;
            }
            catch (DllNotFoundException)
            {
                s_LoadFailed = true;
                StartResult = -1;
            }
            catch (EntryPointNotFoundException)
            {
                s_LoadFailed = true;
                StartResult = -1;
            }
            if (!s_LoadFailed && StartResult != SpaceMouseNative.OK)
                Debug.LogWarning("[SpaceMouse] " + SpaceMouseNative.Describe(StartResult));
            s_LastTime = EditorApplication.timeSinceStartup;
        }

        static void Shutdown()
        {
            if (Connected) SpaceMouseNative.SMB_Stop();
        }

        static void Tick()
        {
            if (!Connected) return;

            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - s_LastTime), 0f, 0.1f);
            s_LastTime = now;

            // Take the device only while Unity is the front app and navigation is on;
            // otherwise hand it back so Blender, Cinema 4D etc. behave normally.
            bool want = SpaceMouseSettings.Enabled && InternalEditorUtility.isApplicationActive;
            if ((SpaceMouseNative.SMB_IsActive() != 0) != want)
                SpaceMouseNative.SMB_SetActive(want ? 1 : 0);

            SpaceMouseNative.SMB_GetState(RawAxes, out uint buttons);
            RawButtons = buttons;
            ComputeChannels();

            uint pressed = buttons & ~s_PrevButtons;
            s_PrevButtons = buttons;

            if (!want) return;

            var view = TargetView();
            if (view == null) return;

            for (int b = 0; b < 2; b++)
                if ((pressed & (1u << b)) != 0) RunButton(SpaceMouseSettings.Button(b), view);

            if (dt > 0f && HasMotion()) Navigate(view, dt);
        }

        static SceneView TargetView()
        {
            if (EditorWindow.mouseOverWindow is SceneView hovered) return hovered;
            if (EditorWindow.focusedWindow is SceneView focused) return focused;
            return SceneView.lastActiveSceneView;
        }

        static void ComputeChannels()
        {
            float full = SpaceMouseSettings.FullScale;
            float dead = SpaceMouseSettings.DeadZone;
            float curve = SpaceMouseSettings.Curve;
            int strongest = -1;
            float strongestAbs = 0f;

            for (int c = 0; c < 6; c++)
            {
                var ch = (Channel)c;
                float v = 0f;
                if (SpaceMouseSettings.ChannelEnabled(ch))
                {
                    v = Mathf.Clamp(RawAxes[SpaceMouseSettings.Source(ch)] / full, -1f, 1f);
                    if (SpaceMouseSettings.Invert(ch)) v = -v;
                    float a = Mathf.Abs(v);
                    a = a <= dead ? 0f : (a - dead) / (1f - dead);
                    v = Mathf.Sign(v) * Mathf.Pow(a, curve);
                }
                Channels[c] = v;
                if (Mathf.Abs(v) > strongestAbs) { strongestAbs = Mathf.Abs(v); strongest = c; }
            }

            if (SpaceMouseSettings.DominantAxis && strongest >= 0)
                for (int c = 0; c < 6; c++) if (c != strongest) Channels[c] = 0f;
        }

        static bool HasMotion()
        {
            for (int c = 0; c < 6; c++) if (Channels[c] != 0f) return true;
            return false;
        }

        static void Navigate(SceneView view, float dt)
        {
            Vector3 pivot = view.pivot;
            Quaternion rot = view.rotation;
            float size = view.size;
            float dist = view.cameraDistance;

            // Push harder, go disproportionately faster: a light touch stays precise,
            // a full push covers the whole environment.
            float right = Boost(Channels[(int)Channel.MoveRight]);
            float up = Boost(Channels[(int)Channel.MoveUp]);
            float fwd = Boost(Channels[(int)Channel.MoveForward]);
            float look = Channels[(int)Channel.LookUp];
            float turn = Channels[(int)Channel.TurnRight];
            float roll = SpaceMouseSettings.LockHorizon ? 0f : Channels[(int)Channel.Roll];

            // Speeds scale with how far the camera is from its pivot, so the
            // same push feels right on a tiny prop and on a whole level.
            float scale = Mathf.Max(dist, 0.5f);
            float move = 1.2f * scale * SpaceMouseSettings.MoveSpeed * dt;
            float degrees = 110f * SpaceMouseSettings.RotateSpeed * dt;

            if (view.in2DMode)
            {
                pivot += rot * new Vector3(right, up, 0f) * move;
                size *= Mathf.Exp(-fwd * 1.5f * SpaceMouseSettings.ZoomSpeed * dt);
                view.LookAtDirect(pivot, rot, Mathf.Max(size, 0.001f));
                view.Repaint();
                return;
            }

            bool orbit = SpaceMouseSettings.Mode == NavigationMode.Orbit;

            if (orbit)
            {
                // Object-style: move and turn the scene as if holding it.
                pivot += rot * new Vector3(right, up, 0f) * move;
                size *= Mathf.Exp(-fwd * 1.5f * SpaceMouseSettings.ZoomSpeed * dt);
                rot = Rotate(rot, -look * degrees, -turn * degrees, -roll * degrees);
                view.LookAtDirect(pivot, rot, Mathf.Max(size, 0.001f));
            }
            else
            {
                // Camera-style fly: the camera moves and turns around itself.
                Vector3 camPos = pivot - rot * Vector3.forward * dist;
                camPos += rot * new Vector3(right, up, fwd) * move;
                rot = Rotate(rot, look * degrees, turn * degrees, roll * degrees);
                pivot = camPos + rot * Vector3.forward * dist;
                // An orthographic view has no depth, so pushing forward would
                // show nothing: zoom instead.
                if (view.orthographic)
                    size = Mathf.Max(size * Mathf.Exp(-fwd * 1.5f * SpaceMouseSettings.ZoomSpeed * dt), 0.001f);
                view.LookAtDirect(pivot, rot, size);
            }
            view.Repaint();
        }

        static float Boost(float v)
        {
            float a = Mathf.Abs(v);
            return v * (1f + (SpaceMouseSettings.PushBoost - 1f) * a * a);
        }

        static Quaternion Rotate(Quaternion rot, float lookUpDeg, float turnRightDeg, float rollDeg)
        {
            // Turn around world up (keeps the horizon level), look around the camera's right axis.
            Quaternion r = Quaternion.AngleAxis(turnRightDeg, Vector3.up) * rot;
            Quaternion pitched = r * Quaternion.AngleAxis(-lookUpDeg, Vector3.right);

            // Stop just short of straight up/down so the view never flips over.
            float tilt = Vector3.Angle(pitched * Vector3.forward, Vector3.up);
            if (SpaceMouseSettings.LockHorizon && (tilt < 2f || tilt > 178f)) pitched = r;

            if (rollDeg != 0f) pitched *= Quaternion.AngleAxis(-rollDeg, Vector3.forward);
            else if (SpaceMouseSettings.LockHorizon) pitched = Level(pitched);
            return pitched;
        }

        // Removes any roll so the horizon stays flat.
        static Quaternion Level(Quaternion q)
        {
            Vector3 f = q * Vector3.forward;
            if (Mathf.Abs(Vector3.Dot(f, Vector3.up)) > 0.999f) return q;
            return Quaternion.LookRotation(f, Vector3.up);
        }

        // Back to Unity's starting view: perspective, looking at the world origin from above and to the side.
        public static void Recenter(SceneView view)
        {
            if (view == null) return;
            view.in2DMode = false;
            view.orthographic = false;
            view.LookAt(Vector3.zero, Quaternion.LookRotation(new Vector3(-1f, -0.7f, -1f)), 10f, false);
            view.Repaint();
        }

        [MenuItem("Tools/SpaceMouse/Recenter Scene View")]
        static void RecenterMenu()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null && SceneView.sceneViews.Count > 0) view = (SceneView)SceneView.sceneViews[0];
            Recenter(view);
        }

        static void RunButton(ButtonAction action, SceneView view)
        {
            switch (action)
            {
                case ButtonAction.FrameSelected:
                    if (Selection.activeGameObject != null) view.FrameSelected();
                    break;
                case ButtonAction.ToggleMode:
                    SpaceMouseSettings.Mode = SpaceMouseSettings.Mode == NavigationMode.Fly ? NavigationMode.Orbit : NavigationMode.Fly;
                    view.ShowNotification(new GUIContent("SpaceMouse: " + SpaceMouseSettings.Mode + " mode"), 1.2);
                    break;
                case ButtonAction.ToggleRoll:
                    SpaceMouseSettings.LockHorizon = !SpaceMouseSettings.LockHorizon;
                    view.ShowNotification(new GUIContent(SpaceMouseSettings.LockHorizon ? "Horizon locked" : "Roll enabled"), 1.2);
                    break;
                case ButtonAction.ResetSpeed:
                    SpaceMouseSettings.MoveSpeed = 1f;
                    SpaceMouseSettings.RotateSpeed = 1f;
                    SpaceMouseSettings.ZoomSpeed = 1f;
                    view.ShowNotification(new GUIContent("SpaceMouse speeds reset"), 1.2);
                    break;
                case ButtonAction.RecenterView:
                    Recenter(view);
                    break;
                case ButtonAction.OriginalSettings:
                    SpaceMouseSettings.ResetAll();
                    view.ShowNotification(new GUIContent("SpaceMouse: back to original settings"), 1.5);
                    break;
            }
        }
    }
}
#endif
