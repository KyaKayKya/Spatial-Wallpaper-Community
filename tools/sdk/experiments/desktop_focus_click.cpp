// Mechanism probe: actual visible Explorer blank click, then an owned receiver.
// Does not click icons, read names, install hooks, or change foreground policy.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <oleacc.h>
#include <shldisp.h>
#include <cstdio>
#include <cstring>
#include <initializer_list>
#pragma comment(lib,"oleacc.lib")
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"oleaut32.lib")
#pragma comment(lib,"uuid.lib")
static HWND listWindow,target,other;static DWORD shellPid;static unsigned targetDown,targetUp,otherDown,otherUp,targetMouseDown,targetMouseUp,routedDown,routedUp;static HHOOK hook;static double maxQueryMs;
static LRESULT CALLBACK Procedure(HWND window,UINT message,WPARAM w,LPARAM l){
 if(window==target&&message==WM_LBUTTONDOWN)targetMouseDown++;if(window==target&&message==WM_LBUTTONUP)targetMouseUp++;
 if(w==VK_SPACE&&message==WM_KEYDOWN){if(window==target)targetDown++;if(window==other)otherDown++;}
 if(w==VK_SPACE&&message==WM_KEYUP){if(window==target)targetUp++;if(window==other)otherUp++;}
 return DefWindowProcW(window,message,w,l);
}
static void Pump(unsigned ms){auto until=GetTickCount64()+ms;do{MSG m;while(PeekMessageW(&m,nullptr,0,0,PM_REMOVE)){TranslateMessage(&m);DispatchMessageW(&m);}Sleep(1);}while(GetTickCount64()<until);}
static BOOL CALLBACK FindDesktop(HWND window,LPARAM){HWND view=FindWindowExW(window,nullptr,L"SHELLDLL_DefView",nullptr);HWND list=view?FindWindowExW(view,nullptr,L"SysListView32",nullptr):nullptr;DWORD pid=0;GetWindowThreadProcessId(list,&pid);if(list&&shellPid&&pid==shellPid)listWindow=list;return TRUE;}
static bool Blank(POINT point){
 if(WindowFromPoint(point)!=listWindow)return false;
 IAccessible* object=nullptr;VARIANT child{},role{};HWND owner=nullptr;
 HRESULT a=AccessibleObjectFromPoint(point,&object,&child),b=object?WindowFromAccessibleObject(object,&owner):E_FAIL,c=object?object->get_accRole(child,&role):E_FAIL;
 bool valid=SUCCEEDED(a)&&SUCCEEDED(b)&&SUCCEEDED(c)&&owner==listWindow&&child.vt==VT_I4&&child.lVal==CHILDID_SELF&&role.vt==VT_I4&&role.lVal==ROLE_SYSTEM_LIST&&WindowFromPoint(point)==listWindow;
 VariantClear(&child);VariantClear(&role);if(object)object->Release();return valid;
}
static bool Owns(HWND window){GUITHREADINFO gui{sizeof(gui)};return GetForegroundWindow()==window&&GetGUIThreadInfo(GetCurrentThreadId(),&gui)&&gui.hwndActive==window&&gui.hwndFocus==window;}
static bool Space(HWND window){if(!Owns(window))return false;INPUT events[2]{};events[0].type=events[1].type=INPUT_KEYBOARD;events[0].ki.wVk=events[1].ki.wVk=VK_SPACE;events[1].ki.dwFlags=KEYEVENTF_KEYUP;return SendInput(2,events,sizeof(INPUT))==2;}
// Experiment only: synchronous Explorer COM in a global hook is NOT a production
// design. A production hit-test service needs an independently bounded deadline.
static LRESULT CALLBACK MouseHook(int code,WPARAM message,LPARAM value){
 if(code==HC_ACTION&&(message==WM_LBUTTONDOWN||message==WM_LBUTTONUP)&&Owns(target)){
  auto data=reinterpret_cast<MSLLHOOKSTRUCT*>(value);LARGE_INTEGER a{},b{},frequency{};QueryPerformanceFrequency(&frequency);QueryPerformanceCounter(&a);bool blank=Blank(data->pt);QueryPerformanceCounter(&b);double elapsed=1000.0*(b.QuadPart-a.QuadPart)/frequency.QuadPart;if(elapsed>maxQueryMs)maxQueryMs=elapsed;
  if(blank&&Owns(target)){if(message==WM_LBUTTONDOWN)routedDown++;else routedUp++;return 1;}
 }
 return CallNextHookEx(hook,code,message,value);
}
int main(int argc,char** argv){
 SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
 if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 2;
 IShellDispatch* shell=nullptr;HRESULT hr=CoCreateInstance(CLSID_Shell,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&shell));if(FAILED(hr))return 3;
 if(argc==2&&!strcmp(argv[1],"--restore")){hr=shell->UndoMinimizeALL();shell->Release();CoUninitialize();return FAILED(hr)?4:0;}
 HWND previous=GetForegroundWindow();POINT cursor{};GetCursorPos(&cursor);bool minimized=false;int result=1;
 do{
  bool held=false;for(int key:{VK_LBUTTON,VK_RBUTTON,VK_SHIFT,VK_CONTROL,VK_MENU,VK_SPACE,VK_LWIN,VK_RWIN})held|=(GetAsyncKeyState(key)&0x8000)!=0;if(held){result=5;break;}
  GetWindowThreadProcessId(GetShellWindow(),&shellPid);EnumWindows(FindDesktop,0);RECT area{};if(!listWindow||!GetWindowRect(listWindow,&area)){result=6;break;}
  WNDCLASSW cls{};cls.lpfnWndProc=Procedure;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"SpatialWallpaperDesktopFocusProbe";if(!RegisterClassW(&cls)){result=7;break;}
  target=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_LAYERED|WS_EX_TRANSPARENT,cls.lpszClassName,L"SDK desktop focus receiver probe",WS_POPUP,0,0,32,32,nullptr,nullptr,cls.hInstance,nullptr);
  if(!target||!SetLayeredWindowAttributes(target,0,0,LWA_ALPHA)){result=8;break;}
  if(FAILED(shell->MinimizeAll())){result=9;break;}minimized=true;Pump(400);ShowWindow(target,SW_SHOWNOACTIVATE);
  POINT point{};bool found=false;for(int y=area.top+80;y<area.bottom&&!found;y+=120)for(int x=area.left+160;x<area.right&&!found;x+=120){POINT p{x,y};if(Blank(p)){point=p;found=true;}}
  if(!found){result=10;break;}SetCursorPos(point.x,point.y);Pump(30);if(!Blank(point)){result=11;break;}
  INPUT clicks[2]{};clicks[0].type=clicks[1].type=INPUT_MOUSE;clicks[0].mi.dwFlags=MOUSEEVENTF_LEFTDOWN;clicks[1].mi.dwFlags=MOUSEEVENTF_LEFTUP;bool sent=SendInput(2,clicks,sizeof(INPUT))==2;Pump(30);
  DWORD foregroundPid=0;GetWindowThreadProcessId(GetForegroundWindow(),&foregroundPid);bool shellFocused=foregroundPid==shellPid&&Blank(point);
  bool requested=shellFocused&&SetForegroundWindow(target)!=0;Pump(50);bool focused=Owns(target);bool noReplay=targetMouseDown==0&&targetMouseUp==0;
  bool first=focused&&Space(target);Pump(50);bool targetOnly=targetDown==1&&targetUp==1;
  bool noHook=argc==2&&!strcmp(argv[1],"--no-hook");if(!noHook)hook=SetWindowsHookExW(WH_MOUSE_LL,MouseHook,GetModuleHandleW(nullptr),0);
  Pump(GetDoubleClickTime()+50);bool secondBlank=Blank(point)&&SendInput(1,clicks,sizeof(INPUT))==1;Pump(30);bool asyncPrimary=(GetAsyncKeyState(VK_LBUTTON)&0x8000)!=0;secondBlank=SendInput(1,clicks+1,sizeof(INPUT))==1&&secondBlank;Pump(50);bool retained=Owns(target);DWORD afterClickPid=0;GetWindowThreadProcessId(GetForegroundWindow(),&afterClickPid);if(hook){UnhookWindowsHookEx(hook);hook=nullptr;}
  std::printf("{\"secondBlankClick\":%s,\"secondClickReceiverFocus\":%s,\"secondClickShellForeground\":%s,\"receiverMouseDown\":%u,\"receiverMouseUp\":%u,\"routedDown\":%u,\"routedUp\":%u,\"asyncPrimaryWhileConsumed\":%s,\"maxSynchronousQueryMs\":%.3f}\n",secondBlank?"true":"false",retained?"true":"false",afterClickPid==shellPid?"true":"false",targetMouseDown,targetMouseUp,routedDown,routedUp,asyncPrimary?"true":"false",maxQueryMs);
  other=CreateWindowExW(WS_EX_TOOLWINDOW,cls.lpszClassName,L"SDK desktop focus exclusion probe",WS_OVERLAPPEDWINDOW,100,100,320,180,nullptr,nullptr,cls.hInstance,nullptr);ShowWindow(other,SW_SHOWNORMAL);SetForegroundWindow(other);Pump(50);
  bool revoked=!Owns(target)&&Owns(other),second=revoked&&Space(other);Pump(50);bool isolated=targetDown==1&&targetUp==1&&otherDown==1&&otherUp==1;
  std::printf("{\"visibleExplorerBlank\":%s,\"nativeClick\":%s,\"shellActuallyForeground\":%s,\"activationRequested\":%s,\"receiverActuallyFocused\":%s,\"noClickReplay\":%s,\"receiverKeyPair\":%s,\"otherWindowRevokes\":%s,\"otherWindowKeyPair\":%s,\"isolated\":%s}\n",found?"true":"false",sent?"true":"false",shellFocused?"true":"false",requested?"true":"false",focused?"true":"false",noReplay?"true":"false",first&&targetOnly?"true":"false",revoked?"true":"false",second?"true":"false",isolated?"true":"false");
  result=sent&&shellFocused&&focused&&noReplay&&first&&targetOnly&&secondBlank&&retained&&routedDown==1&&routedUp==1&&revoked&&second&&isolated?0:12;
 }while(false);
 if(hook)UnhookWindowsHookEx(hook);if(target)DestroyWindow(target);if(other)DestroyWindow(other);if(minimized)shell->UndoMinimizeALL();SetCursorPos(cursor.x,cursor.y);if(previous&&IsWindow(previous))SetForegroundWindow(previous);shell->Release();CoUninitialize();return result;
}
