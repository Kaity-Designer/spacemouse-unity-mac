// Loads the built bundle exactly like Unity would and logs device input.
// Usage: ./test_harness [seconds] [--guided]
// --guided speaks one motion at a time and reports which axis responded.

#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <unistd.h>

typedef int      (*StartFn)(const char *, uint32_t);
typedef int      (*SetActiveFn)(int);
typedef uint32_t (*GetStateFn)(int32_t *, uint32_t *);
typedef int      (*CountFn)(void);
typedef void     (*StopFn)(void);

static const char *kNames[6] = {"tx", "ty", "tz", "rx", "ry", "rz"};

static void say(const char *text) {
    char cmd[256];
    snprintf(cmd, sizeof cmd, "say '%s' &", text);
    system(cmd);
    printf("\n>>> %s\n", text);
    fflush(stdout);
}

int main(int argc, char **argv) {
    int seconds = argc > 1 ? atoi(argv[1]) : 10;
    int guided = argc > 2 && strcmp(argv[2], "--guided") == 0;

    const char *path = "../Plugins/macOS/SpaceMouseBridge.bundle/Contents/MacOS/SpaceMouseBridge";
    void *lib = dlopen(path, RTLD_NOW);
    if (!lib) { fprintf(stderr, "dlopen failed: %s\n", dlerror()); return 1; }

    StartFn start = (StartFn)dlsym(lib, "SMB_Start");
    SetActiveFn setActive = (SetActiveFn)dlsym(lib, "SMB_SetActive");
    GetStateFn getState = (GetStateFn)dlsym(lib, "SMB_GetState");
    CountFn count = (CountFn)dlsym(lib, "SMB_DeviceCount");
    StopFn stop = (StopFn)dlsym(lib, "SMB_Stop");
    typedef void (*DebugFn)(uint32_t *, uint32_t *, uint32_t *, uint32_t *, uint32_t *, uint32_t *);
    DebugFn dbg = (DebugFn)dlsym(lib, "SMB_GetDebug");
    if (!start || !setActive || !getState || !count || !stop) {
        fprintf(stderr, "missing exports\n"); return 1;
    }

    int rc = start("SpaceMouseBridgeTest", 0);  // manual mode: the harness is never the front app
    printf("SMB_Start -> %d\n", rc);
    if (rc != 0) return 2;
    printf("SMB_SetActive(1) -> %d\n", setActive(1));
    usleep(300 * 1000);
    printf("devices reported: %d\n", count());

    const char *steps[] = {
        "Push the cap to the right", "Pull the cap toward you",
        "Lift the cap up", "Tilt the cap forward, away from you",
        "Tilt the cap to the right", "Twist the cap clockwise",
        "Press the left button", "Press the right button",
    };
    int nSteps = guided ? 8 : seconds / 2;
    int perStep = guided ? seconds : 2;
    if (!guided) { say("Start wiggling the cap now, and press the buttons"); }
    if (guided) { say("Get ready. Hold each move until the next prompt, then let go."); sleep(5); }

    uint32_t lastSeq = 0, msgs = 0;
    int32_t peak[6] = {0}, minv[6] = {0}, maxv[6] = {0};
    for (int s = 0; s < nSteps; s++) {
        if (guided) { say(steps[s]); memset(peak, 0, sizeof peak); }
        uint32_t seenButtons = 0;
        for (int t = 0; t < perStep * 100; t++) {  // 10 ms polling
            int32_t ax[6]; uint32_t b;
            uint32_t seq = getState(ax, &b);
            if (seq != lastSeq) {
                msgs += seq - lastSeq; lastSeq = seq;
                seenButtons |= b;
                for (int i = 0; i < 6; i++) {
                    if (abs(ax[i]) > abs(peak[i])) peak[i] = ax[i];
                    if (ax[i] < minv[i]) minv[i] = ax[i];
                    if (ax[i] > maxv[i]) maxv[i] = ax[i];
                }
                if (!guided && (t % 25 == 0))
                    printf("axes %5d %5d %5d | %5d %5d %5d  buttons 0x%08x\n",
                           ax[0], ax[1], ax[2], ax[3], ax[4], ax[5], b);
            }
            usleep(10 * 1000);
        }
        if (dbg) {
            uint32_t cb, other, type, cl, cmd, ours;
            dbg(&cb, &other, &type, &cl, &cmd, &ours);
            printf("  debug: callbacks=%u otherClient=%u lastType=0x%08x lastClient=%u lastCmd=%u ourClient=%u | front app: ",
                   cb, other, type, cl, cmd, ours);
            fflush(stdout);
            system("lsappinfo info -only name \"$(lsappinfo front)\" | head -1");
        }
        if (guided) {
            int best = 0;
            for (int i = 1; i < 6; i++) if (abs(peak[i]) > abs(peak[best])) best = i;
            if (s < 6)
                printf("RESULT step=%d \"%s\": strongest %s = %d   (all peaks: %d %d %d | %d %d %d)\n",
                       s, steps[s], kNames[best], peak[best],
                       peak[0], peak[1], peak[2], peak[3], peak[4], peak[5]);
            else
                printf("RESULT step=%d \"%s\": buttons seen 0x%08x\n", s, steps[s], seenButtons);
        }
    }
    printf("\nmessages received: %u\nrange per axis:", msgs);
    for (int i = 0; i < 6; i++) printf(" %s[%d..%d]", kNames[i], minv[i], maxv[i]);
    printf("\n");
    if (guided) say("Done, thank you");

    stop();
    printf("stopped cleanly\n");
    return 0;
}
