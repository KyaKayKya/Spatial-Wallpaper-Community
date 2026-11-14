#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>

// Mechanism experiment only. These owned windows are not Explorer or a product
// presenter. Never infer desktop hit-test correctness from this fixture.
static HWND target, other;
static unsigned targetDown, targetUp, otherDown, otherUp, lost;
static LRESULT CALLBACK Procedure(HWND hwnd, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_KEYDOWN && w == VK_SPACE) {
        if (hwnd == target) ++targetDown; else if (hwnd == other) ++otherDown;
        return 0;
    }
    if (message == WM_KEYUP && w == VK_SPACE) {
        if (hwnd == target) ++targetUp; else if (hwnd == other) ++otherUp;
        return 0;
    }
    if (message == WM_KILLFOCUS && hwnd == target) ++lost;
    return DefWindowProcW(hwnd, message, w, l);
}
static void Pump(unsigned milliseconds) {
    ULONGLONG end = GetTickCount64() + milliseconds;
    do {
        MSG msg;
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&msg); DispatchMessageW(&msg);
        }
        Sleep(1);
    } while (GetTickCount64() < end);
}
static bool OwnsFocus(HWND hwnd) {
    GUITHREADINFO info = { sizeof(info) };
    return GetForegroundWindow() == hwnd &&
        GetGUIThreadInfo(GetCurrentThreadId(), &info) &&
        info.hwndActive == hwnd && info.hwndFocus == hwnd;
}
static bool Space(HWND hwnd) {
    // One paired batch, only after verifying an owned foreground window. No
    // synthetic Alt, AttachThreadInput or foreground-lock workaround.
    if (!OwnsFocus(hwnd)) return false;
    INPUT keys[2] = {};
    keys[0].type = keys[1].type = INPUT_KEYBOARD;
    keys[0].ki.wVk = keys[1].ki.wVk = VK_SPACE;
    keys[1].ki.dwFlags = KEYEVENTF_KEYUP;
    return SendInput(2, keys, sizeof(INPUT)) == 2;
}
int main() {
    HWND previous = GetForegroundWindow();
    WNDCLASSW cls = {};
    cls.lpfnWndProc = Procedure; cls.hInstance = GetModuleHandleW(nullptr);
    cls.lpszClassName = L"SpatialInputFocusProbe";
    if (!RegisterClassW(&cls)) return 2;
    other = CreateWindowExW(WS_EX_TOOLWINDOW, cls.lpszClassName,
        L"SDK input mechanism fixture", WS_OVERLAPPEDWINDOW,
        80, 80, 320, 180, nullptr, nullptr, cls.hInstance, nullptr);
    target = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT,
        cls.lpszClassName, L"SDK invisible focus receiver", WS_POPUP,
        100, 100, 40, 40, nullptr, nullptr, cls.hInstance, nullptr);
    if (!other || !target) return 2;
    bool layered = SetLayeredWindowAttributes(target, 0, 0, LWA_ALPHA) != 0;
    ShowWindow(other, SW_SHOWNORMAL); SetForegroundWindow(other); Pump(100);
    bool initial = OwnsFocus(other);
    ShowWindow(target, SW_SHOWNOACTIVATE);
    bool requested = SetForegroundWindow(target) != 0;
    Pump(100);
    bool focused = OwnsFocus(target);
    bool first = focused && Space(target); Pump(100);
    bool targetOnly = targetDown == 1 && targetUp == 1 && otherDown == 0 && otherUp == 0;
    SetForegroundWindow(other); Pump(100);
    bool revoked = !OwnsFocus(target) && OwnsFocus(other) && lost != 0;
    bool second = revoked && Space(other); Pump(100);
    bool isolated = targetDown == 1 && targetUp == 1 && otherDown == 1 && otherUp == 1;
    DestroyWindow(target); DestroyWindow(other);
    if (previous && IsWindow(previous)) SetForegroundWindow(previous);
    printf("{\"fixtureOnly\":true,\"layered\":%s,\"initialFocus\":%s,"
        "\"activationRequested\":%s,\"actualFocus\":%s,\"targetOnly\":%s,"
        "\"revoked\":%s,\"isolated\":%s,\"targetDown\":%u,\"targetUp\":%u,"
        "\"otherDown\":%u,\"otherUp\":%u}\n",
        layered?"true":"false",initial?"true":"false",requested?"true":"false",
        focused?"true":"false",targetOnly?"true":"false",revoked?"true":"false",
        isolated?"true":"false",targetDown,targetUp,otherDown,otherUp);
    if (!initial || !focused) return 4; // OS refused foreground: unsupported run, not pass.
    return layered && first && second && targetOnly && revoked && isolated ? 0 : 1;
}
