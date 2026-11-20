// SpaceMouseBridge: a tiny C bridge between the installed 3Dconnexion macOS
// driver and the Unity Editor.
//
// It loads /Library/Frameworks/3DconnexionClient.framework at runtime (dlopen),
// so nothing from 3Dconnexion is compiled into or shipped with this plugin, and
// Unity still loads fine on a Mac without the driver (SMB_Start just fails).
//
// The driver delivers device state on its own thread; we keep the latest
// axes and buttons behind a lock and Unity polls them every editor tick.

#include <dlfcn.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>
#include <os/lock.h>

// ---------------------------------------------------------------------------
// Minimal declarations for the driver's public client API, written from the
// documented ABI (function signatures, constants, struct layout).

#define CNX_SIGNATURE_MANUAL      0x2B2B2B2Bu  // '++++' client is activated by us
#define CNX_MODE_TAKEOVER         1
#define CNX_MASK_ALL              0x3FFFu
#define CNX_MASK_ALL_BUTTONS      0xFFFFFFFFu
#define CNX_MSG_DEVICE_STATE      0x33645352u  // '3dSR'
#define CNX_CTL_ACTIVATE_CLIENT   0x33646163u  // '3dac'
#define CNX_CTL_DEACTIVATE_CLIENT 0x33646463u  // '3ddc'

#pragma pack(push, 2)
typedef struct {
    uint16_t version;
    uint16_t client;
    uint16_t command;
    int16_t  param;
    int32_t  value;
    uint64_t time;
    uint8_t  report[8];
    uint16_t buttons8;
    int16_t  axis[6];   // tx, ty, tz, rx, ry, rz
    uint16_t address;
    uint32_t buttons;
} CnxDeviceState;
#pragma pack(pop)

typedef void (*CnxAddedProc)(unsigned int productID);
typedef void (*CnxRemovedProc)(unsigned int productID);
typedef void (*CnxMessageProc)(unsigned int productID, unsigned int type, void *arg);

typedef int16_t  (*SetHandlersFn)(CnxMessageProc, CnxAddedProc, CnxRemovedProc, bool);
typedef void     (*CleanupHandlersFn)(void);
typedef uint16_t (*RegisterClientFn)(uint32_t, uint8_t *, uint16_t, uint32_t);
typedef void     (*SetButtonMaskFn)(uint16_t, uint32_t);
typedef void     (*UnregisterClientFn)(uint16_t);
typedef int16_t  (*ClientControlFn)(uint16_t, uint32_t, int32_t, int32_t *);

static const char *kFrameworkPath =
    "/Library/Frameworks/3DconnexionClient.framework/3DconnexionClient";

static void              *gLib;
static SetHandlersFn      pSetHandlers;
static CleanupHandlersFn  pCleanupHandlers;
static RegisterClientFn   pRegisterClient;
static SetButtonMaskFn    pSetButtonMask;
static UnregisterClientFn pUnregisterClient;
static ClientControlFn    pClientControl;

// ---------------------------------------------------------------------------
// Shared state

static os_unfair_lock gLock = OS_UNFAIR_LOCK_INIT;
static bool     gStarted;
static bool     gActive;
static uint16_t gClientID;
static int16_t  gAxes[6];
static uint32_t gButtons;
static uint32_t gSequence;      // bumps on every device message
static int      gDeviceCount;   // devices reported by the driver's added/removed callbacks
static uint64_t gLastTime;

// Diagnostics
static uint32_t gDbgCallbacks, gDbgOtherClient, gDbgLastType;
static uint16_t gDbgLastClient, gDbgLastCommand;

static void OnMessage(unsigned int productID, unsigned int type, void *arg) {
    (void)productID;
    os_unfair_lock_lock(&gLock);
    gDbgCallbacks++;
    gDbgLastType = type;
    os_unfair_lock_unlock(&gLock);
    if (type != CNX_MSG_DEVICE_STATE || arg == NULL) return;
    const CnxDeviceState *s = (const CnxDeviceState *)arg;
    os_unfair_lock_lock(&gLock);
    gDbgLastClient = s->client;
    gDbgLastCommand = s->command;
    if (gClientID != 0 && s->client != 0 && s->client != gClientID) {
        gDbgOtherClient++;
        os_unfair_lock_unlock(&gLock);  // message meant for another client
        return;
    }
    memcpy(gAxes, s->axis, sizeof(gAxes));
    gButtons = s->buttons;
    gLastTime = s->time;
    gSequence++;
    os_unfair_lock_unlock(&gLock);
}

static void OnAdded(unsigned int productID) {
    (void)productID;
    os_unfair_lock_lock(&gLock);
    gDeviceCount++;
    os_unfair_lock_unlock(&gLock);
}

static void OnRemoved(unsigned int productID) {
    (void)productID;
    os_unfair_lock_lock(&gLock);
    if (gDeviceCount > 0) gDeviceCount--;
    memset(gAxes, 0, sizeof(gAxes));
    gButtons = 0;
    os_unfair_lock_unlock(&gLock);
}

static bool LoadDriver(void) {
    if (gLib) return true;
    gLib = dlopen(kFrameworkPath, RTLD_NOW | RTLD_LOCAL);
    if (!gLib) return false;
    pSetHandlers      = (SetHandlersFn)dlsym(gLib, "SetConnexionHandlers");
    pCleanupHandlers  = (CleanupHandlersFn)dlsym(gLib, "CleanupConnexionHandlers");
    pRegisterClient   = (RegisterClientFn)dlsym(gLib, "RegisterConnexionClient");
    pSetButtonMask    = (SetButtonMaskFn)dlsym(gLib, "SetConnexionClientButtonMask");
    pUnregisterClient = (UnregisterClientFn)dlsym(gLib, "UnregisterConnexionClient");
    pClientControl    = (ClientControlFn)dlsym(gLib, "ConnexionClientControl");
    if (!pSetHandlers || !pCleanupHandlers || !pRegisterClient ||
        !pUnregisterClient || !pClientControl) {
        dlclose(gLib);
        gLib = NULL;
        return false;
    }
    return true;
}

// ---------------------------------------------------------------------------
// Exported API (called from C# via DllImport)

enum {
    SMB_OK                 = 0,
    SMB_ERR_NO_DRIVER      = 1,  // 3DconnexionClient.framework missing or incomplete
    SMB_ERR_HANDLERS       = 2,  // SetConnexionHandlers failed
    SMB_ERR_REGISTER       = 3,  // RegisterConnexionClient returned 0
};

static uint32_t gSignature;

// Safe to call repeatedly (Unity reloads scripts but keeps this library loaded).
// clientName: the app's executable name (Pascal-encoded for the driver), max 63 chars.
// signature: the app's 4-char creator code (Unity Editor = 'UNED'), so the driver
// routes input to us whenever that app is frontmost. Pass 0 for manual mode,
// where SMB_SetActive decides when we hold the device.
__attribute__((visibility("default")))
int SMB_Start(const char *clientName, uint32_t signature) {
    if (gStarted) return SMB_OK;
    if (!LoadDriver()) return SMB_ERR_NO_DRIVER;

    if (pSetHandlers(OnMessage, OnAdded, OnRemoved, true) != 0)
        return SMB_ERR_HANDLERS;

    uint8_t pname[64] = {0};  // Pascal string: length byte, then characters
    size_t len = clientName ? strlen(clientName) : 0;
    if (len > 63) len = 63;
    pname[0] = (uint8_t)len;
    if (len) memcpy(pname + 1, clientName, len);

    uint32_t sig = signature ? signature : CNX_SIGNATURE_MANUAL;
    uint16_t id = pRegisterClient(sig, pname, CNX_MODE_TAKEOVER, CNX_MASK_ALL);
    if (id == 0) {
        pCleanupHandlers();
        return SMB_ERR_REGISTER;
    }
    if (pSetButtonMask) pSetButtonMask(id, CNX_MASK_ALL_BUTTONS);

    os_unfair_lock_lock(&gLock);
    gClientID = id;
    gSignature = sig;
    gStarted = true;
    gActive = false;
    os_unfair_lock_unlock(&gLock);
    return SMB_OK;
}

// Take the device (Unity focused) or hand it back to the driver (Unity in the
// background), so other apps such as Blender keep working normally.
__attribute__((visibility("default")))
int SMB_SetActive(int active) {
    if (!gStarted) return -1;
    bool want = active != 0;
    if (want == gActive) return 0;
    int32_t result = 0;
    int16_t err = 0;
    if (gSignature == CNX_SIGNATURE_MANUAL)  // otherwise the driver follows the front app itself
        err = pClientControl(gClientID,
                             want ? CNX_CTL_ACTIVATE_CLIENT : CNX_CTL_DEACTIVATE_CLIENT,
                             0, &result);
    os_unfair_lock_lock(&gLock);
    gActive = want;
    if (!want) {  // no release message arrives once we let go, so clear it here
        memset(gAxes, 0, sizeof(gAxes));
        gButtons = 0;
    }
    os_unfair_lock_unlock(&gLock);
    return err;
}

// Copies the latest raw axes (tx, ty, tz, rx, ry, rz) and the button bitmask.
// Returns the message sequence number, which changes whenever new data arrives.
__attribute__((visibility("default")))
uint32_t SMB_GetState(int32_t *axesOut6, uint32_t *buttonsOut) {
    os_unfair_lock_lock(&gLock);
    if (axesOut6) for (int i = 0; i < 6; i++) axesOut6[i] = gAxes[i];
    if (buttonsOut) *buttonsOut = gButtons;
    uint32_t seq = gSequence;
    os_unfair_lock_unlock(&gLock);
    return seq;
}

__attribute__((visibility("default")))
int SMB_IsStarted(void) { return gStarted ? 1 : 0; }

__attribute__((visibility("default")))
int SMB_IsActive(void) { return gActive ? 1 : 0; }

__attribute__((visibility("default")))
int SMB_DeviceCount(void) {
    os_unfair_lock_lock(&gLock);
    int n = gDeviceCount;
    os_unfair_lock_unlock(&gLock);
    return n;
}

// callbacks: every driver callback; otherClient: device states addressed to a different client
__attribute__((visibility("default")))
void SMB_GetDebug(uint32_t *callbacks, uint32_t *otherClient, uint32_t *lastType,
                  uint32_t *lastClient, uint32_t *lastCommand, uint32_t *ourClient) {
    os_unfair_lock_lock(&gLock);
    if (callbacks) *callbacks = gDbgCallbacks;
    if (otherClient) *otherClient = gDbgOtherClient;
    if (lastType) *lastType = gDbgLastType;
    if (lastClient) *lastClient = gDbgLastClient;
    if (lastCommand) *lastCommand = gDbgLastCommand;
    if (ourClient) *ourClient = gClientID;
    os_unfair_lock_unlock(&gLock);
}

__attribute__((visibility("default")))
void SMB_Stop(void) {
    if (!gStarted) return;
    SMB_SetActive(0);
    pUnregisterClient(gClientID);
    pCleanupHandlers();
    os_unfair_lock_lock(&gLock);
    gStarted = false;
    gClientID = 0;
    gSignature = 0;
    gDeviceCount = 0;
    memset(gAxes, 0, sizeof(gAxes));
    gButtons = 0;
    os_unfair_lock_unlock(&gLock);
}
