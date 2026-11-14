#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
static LRESULT CALLBACK Window(HWND h,UINT m,WPARAM w,LPARAM l){
 if(m==WM_CLOSE){DestroyWindow(h);return 0;}
 if(m==WM_DESTROY){PostQuitMessage(0);return 0;}
 if(m==WM_KEYDOWN||m==WM_KEYUP)return 0;
 return DefWindowProcW(h,m,w,l);
}
int main(){
 WNDCLASSW c={};c.lpfnWndProc=Window;c.hInstance=GetModuleHandleW(nullptr);c.lpszClassName=L"MiniShotOwnedFocusGuard";
 if(!RegisterClassW(&c))return 2;
 HWND h=CreateWindowExW(0,c.lpszClassName,L"MiniShot test: other owned window",WS_OVERLAPPEDWINDOW,40,40,340,180,nullptr,nullptr,c.hInstance,nullptr);
 if(!h)return 2;ShowWindow(h,SW_SHOWNORMAL);SetForegroundWindow(h);
 printf("{\"pid\":%lu,\"window\":%llu}\n",GetCurrentProcessId(),(unsigned long long)(uintptr_t)h);fflush(stdout);
 MSG m;while(GetMessageW(&m,nullptr,0,0)>0){TranslateMessage(&m);DispatchMessageW(&m);}return 0;
}
