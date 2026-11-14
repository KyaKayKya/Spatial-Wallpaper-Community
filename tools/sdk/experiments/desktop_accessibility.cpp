// Read-only mechanism probe. Never reads accessible names or sends input.
#include <windows.h>
#include <oleacc.h>
#include <commctrl.h>
#include <cstdio>
#include <cstring>
#pragma comment(lib, "oleacc.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")
#pragma comment(lib, "comctl32.lib")
#pragma comment(lib, "uuid.lib")

static HWND desktopList;
static BOOL CALLBACK findDesktop(HWND window, LPARAM) {
    HWND view = FindWindowExW(window, nullptr, L"SHELLDLL_DefView", nullptr);
    HWND list = view ? FindWindowExW(view, nullptr, L"SysListView32", nullptr) : nullptr;
    DWORD shellPid = 0, candidatePid = 0;
    GetWindowThreadProcessId(GetShellWindow(), &shellPid);
    GetWindowThreadProcessId(list, &candidatePid);
    if (list && shellPid && candidatePid == shellPid) desktopList = list;
    return TRUE;
}
static int fixture() {
    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_LISTVIEW_CLASSES};
    if (!InitCommonControlsEx(&controls)) return 5;
    HWND before = GetForegroundWindow();
    HWND host = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
        L"STATIC", L"SDK input mechanism test", WS_POPUP, 20, 20, 320, 200,
        nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    HWND list = CreateWindowExW(0, WC_LISTVIEWW, L"", WS_CHILD | WS_VISIBLE | LVS_ICON,
        0, 0, 320, 200, host, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!host || !list) { if(host) DestroyWindow(host); return 6; }
    LVITEMW entry{};entry.mask=LVIF_TEXT;entry.iItem=0;
    wchar_t label[]=L"SDK fixture";entry.pszText=label;
    SendMessageW(list,LVM_INSERTITEMW,0,reinterpret_cast<LPARAM>(&entry));
    ListView_SetItemPosition(list,0,10,10);
    ShowWindow(host,SW_SHOWNOACTIVATE);UpdateWindow(host);
    RECT item{};ListView_GetItemRect(list,0,&item,LVIR_BOUNDS);
    POINT samples[]={{(item.left+item.right)/2,(item.top+item.bottom)/2},{280,160}};
    bool passed=true;
    for(int i=0;i<2;i++) {
        ClientToScreen(list,&samples[i]);
        IAccessible* accessible=nullptr;VARIANT child{},role{};HWND owner=nullptr;
        HRESULT hr=AccessibleObjectFromPoint(samples[i],&accessible,&child);
        HRESULT wh=accessible?WindowFromAccessibleObject(accessible,&owner):E_FAIL;
        HRESULT rh=accessible?accessible->get_accRole(child,&role):E_FAIL;
        bool hit=WindowFromPoint(samples[i])==list;
        bool known=SUCCEEDED(hr)&&SUCCEEDED(wh)&&SUCCEEDED(rh)&&owner==list&&child.vt==VT_I4&&role.vt==VT_I4;
        bool match=known&&hit&&(i==0?role.lVal==ROLE_SYSTEM_LISTITEM:
            child.lVal==CHILDID_SELF&&role.lVal==ROLE_SYSTEM_LIST);
        std::printf("{\"fixture\":%d,\"hit\":%s,\"owner\":%s,\"child\":%ld,\"role\":%ld,\"pass\":%s}\n",
            i,hit?"true":"false",owner==list?"true":"false",child.vt==VT_I4?child.lVal:-1,
            role.vt==VT_I4?role.lVal:-1,match?"true":"false");
        passed &= match;VariantClear(&child);VariantClear(&role);if(accessible)accessible->Release();
    }
    DestroyWindow(host);
    passed &= GetForegroundWindow()==before;
    return passed?0:7;
}
int main(int argc,char** argv) {
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 2;
    if(argc==2&&std::strcmp(argv[1],"--fixture")==0){int result=fixture();CoUninitialize();return result;}
    EnumWindows(findDesktop, 0);
    RECT area{};
    if (!desktopList || !GetWindowRect(desktopList, &area)) return 3;
    bool direct=argc==2&&std::strcmp(argv[1],"--shell-object")==0;
    IAccessible* shellObject=nullptr;
    if(direct&&FAILED(AccessibleObjectFromWindow(desktopList,OBJID_CLIENT,IID_IAccessible,
        reinterpret_cast<void**>(&shellObject))))return 8;
    unsigned visible = 0, selfList = 0, item = 0, unknown = 0;
    LARGE_INTEGER frequency{}, start{}, end{};
    QueryPerformanceFrequency(&frequency);
    double maximumMs = 0;
    // Sparse physical-pixel grid: no icon labels, application text, or coordinates retained.
    for (int y = area.top + 16; y < area.bottom; y += 160)
    for (int x = area.left + 16; x < area.right; x += 160) {
        POINT point{x, y};
        if (!direct&&WindowFromPoint(point) != desktopList) continue;
        ++visible;
        IAccessible* accessible = nullptr;
        VARIANT child{}, role{};
        QueryPerformanceCounter(&start);
        HRESULT hr;
        if(direct){accessible=shellObject;accessible->AddRef();hr=accessible->accHitTest(x,y,&child);}
        else hr=AccessibleObjectFromPoint(point, &accessible, &child);
        HWND owner = nullptr;
        HRESULT ownerHr = accessible ? WindowFromAccessibleObject(accessible, &owner) : E_FAIL;
        HRESULT roleHr = accessible ? accessible->get_accRole(child, &role) : E_FAIL;
        QueryPerformanceCounter(&end);
        double ms = 1000.0 * (end.QuadPart - start.QuadPart) / frequency.QuadPart;
        if (ms > maximumMs) maximumMs = ms;
        if (SUCCEEDED(hr) && SUCCEEDED(ownerHr) && SUCCEEDED(roleHr) &&
            owner == desktopList && child.vt == VT_I4 && role.vt == VT_I4) {
            if (child.lVal == CHILDID_SELF && role.lVal == ROLE_SYSTEM_LIST) ++selfList;
            else if (role.lVal == ROLE_SYSTEM_LISTITEM) ++item;
            else ++unknown;
        } else ++unknown;
        VariantClear(&role);
        VariantClear(&child);
        if (accessible) accessible->Release();
    }
    std::printf("{\"mode\":\"%s\",\"samples\":%u,\"blankCandidate\":%u,\"item\":%u,\"unknown\":%u,\"maxQueryMs\":%.3f}\n",
        direct?"shell-object-ignores-occlusion":"visible-point",visible, selfList, item, unknown, maximumMs);
    if(shellObject)shellObject->Release();
    CoUninitialize();
    return visible ? 0 : 4;
}
