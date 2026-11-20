#if UNITY_EDITOR_OSX
using System.Runtime.InteropServices;

namespace Kah.SpaceMouse
{
    // P/Invoke surface of Plugins/macOS/SpaceMouseBridge.bundle (source in Native~).
    internal static class SpaceMouseNative
    {
        const string Lib = "SpaceMouseBridge";

        public const int OK = 0, ErrNoDriver = 1, ErrHandlers = 2, ErrRegister = 3;

        public const uint UnityEditorSignature = 0x554E4544; // 'UNED', CFBundleSignature of Unity.app

        [DllImport(Lib)] public static extern int SMB_Start(string clientName, uint signature);
        [DllImport(Lib)] public static extern void SMB_GetDebug(out uint callbacks, out uint otherClient, out uint lastType,
                                                                 out uint lastClient, out uint lastCommand, out uint ourClient);
        [DllImport(Lib)] public static extern int SMB_SetActive(int active);
        [DllImport(Lib)] public static extern uint SMB_GetState([Out] int[] axes6, out uint buttons);
        [DllImport(Lib)] public static extern int SMB_IsStarted();
        [DllImport(Lib)] public static extern int SMB_IsActive();
        [DllImport(Lib)] public static extern int SMB_DeviceCount();
        [DllImport(Lib)] public static extern void SMB_Stop();

        public static string Describe(int code)
        {
            switch (code)
            {
                case OK: return "Connected";
                case ErrNoDriver: return "3Dconnexion driver not found. Install 3DxWare / 3Dconnexion software for macOS.";
                case ErrHandlers: return "The 3Dconnexion driver refused the connection (handlers).";
                case ErrRegister: return "The 3Dconnexion driver refused to register Unity as a client.";
                default: return "Unknown error " + code;
            }
        }
    }
}
#endif
