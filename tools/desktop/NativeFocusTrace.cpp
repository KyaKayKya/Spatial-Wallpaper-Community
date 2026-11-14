#include <windows.h>
#include <cstdio>

struct FocusRecord {
 HWND window,parent;
 DWORD process,thread,eventThread,eventProcess,eventTime;
 ULONGLONG eventCreation,eventCallbackTick;
 wchar_t eventImage[MAX_PATH];
 volatile LONG armed,version;
 wchar_t text[8192];
};
#pragma section(".spfocus",read,write,shared)
__declspec(allocate(".spfocus")) FocusRecord record={};
static HMODULE module;
static HHOOK hook;
static HWINEVENTHOOK eventHook;

static void CALLBACK FocusEvent(HWINEVENTHOOK,DWORD event,HWND window,LONG,LONG,DWORD thread,DWORD time){
 if(event==EVENT_OBJECT_FOCUS&&window==record.parent&&record.armed&&record.eventThread==0){
  record.eventTime=time;record.eventThread=thread;record.eventCallbackTick=GetTickCount64();HANDLE observed=OpenThread(THREAD_QUERY_LIMITED_INFORMATION,FALSE,thread);
  if(observed){record.eventProcess=GetProcessIdOfThread(observed);HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,record.eventProcess);if(process){DWORD size=MAX_PATH;QueryFullProcessImageNameW(process,0,record.eventImage,&size);FILETIME creation,exit,kernel,user;if(GetProcessTimes(process,&creation,&exit,&kernel,&user))record.eventCreation=(static_cast<ULONGLONG>(creation.dwHighDateTime)<<32)|creation.dwLowDateTime;CloseHandle(process);}CloseHandle(observed);}
 }
}

static LRESULT CALLBACK Observe(int code,WPARAM first,LPARAM second){
 if(code>=0&&GetCurrentProcessId()==record.process){
  const auto message=reinterpret_cast<CWPSTRUCT*>(second);
  if(message->hwnd==record.window){
   if(message->message==WM_SETFOCUS)InterlockedExchange(&record.armed,1);
   if(message->message==WM_KILLFOCUS&&record.armed&&InterlockedCompareExchange(&record.version,1,0)==0){
    void* frames[32]={};USHORT count=CaptureStackBackTrace(0,32,frames,nullptr);
    size_t used=swprintf_s(record.text,L"tick=%llu;thread=%lu;focus-to=%p;in-send=%lu;stack=",GetTickCount64(),GetCurrentThreadId(),reinterpret_cast<void*>(message->wParam),InSendMessageEx(nullptr));
    for(USHORT index=0;index<count&&used<7600;index++){
     HMODULE owner=nullptr;wchar_t name[MAX_PATH]={};
     if(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(frames[index]),&owner)&&GetModuleFileNameW(owner,name,MAX_PATH)){
      const wchar_t* filename=wcsrchr(name,L'\\');filename=filename?filename+1:name;
      used+=swprintf_s(record.text+used,8192-used,L"%s+%llx;",filename,reinterpret_cast<ULONG_PTR>(frames[index])-reinterpret_cast<ULONG_PTR>(owner));
     }else used+=swprintf_s(record.text+used,8192-used,L"%p;",frames[index]);
    }
    InterlockedExchange(&record.version,2);
   }
  }
 }
 return CallNextHookEx(nullptr,code,first,second);
}

extern "C" __declspec(dllexport) int __cdecl BeginFocusTrace(DWORD thread,HWND window){
 DWORD process=0;GUITHREADINFO focus={sizeof(GUITHREADINFO)};if(hook||GetWindowThreadProcessId(window,&process)!=thread||!process||!GetGUIThreadInfo(thread,&focus)||focus.hwndFocus!=window)return 0;
 record.window=window;record.parent=GetAncestor(window,GA_ROOT);record.process=process;record.thread=thread;record.armed=1;record.version=0;record.text[0]=0;record.eventThread=record.eventProcess=record.eventTime=0;record.eventCreation=record.eventCallbackTick=0;record.eventImage[0]=0;
 hook=SetWindowsHookExW(WH_CALLWNDPROC,Observe,module,thread);if(!hook)return 0;
 eventHook=SetWinEventHook(EVENT_OBJECT_FOCUS,EVENT_OBJECT_FOCUS,nullptr,FocusEvent,0,0,WINEVENT_OUTOFCONTEXT);
 if(!eventHook){if(UnhookWindowsHookEx(hook))hook=nullptr;return 0;}return 1;
}
extern "C" __declspec(dllexport) int __cdecl ReadFocusTrace(wchar_t* text,int capacity){
 if(!text||capacity<1)return -1;if(InterlockedCompareExchange(&record.version,0,0)!=2){text[0]=0;return 0;}
 if(wcscpy_s(text,capacity,record.text)!=0)return -1;size_t used=wcslen(text);return _snwprintf_s(text+used,capacity-used,_TRUNCATE,L"focus-event:thread=%lu;process=%lu;time=%lu;callback=%llu;creation=%llu;image=%s;",record.eventThread,record.eventProcess,record.eventTime,record.eventCallbackTick,record.eventCreation,record.eventImage)>=0?2:-1;
}
extern "C" __declspec(dllexport) int __cdecl EndFocusTrace(){
 bool closed=true;if(eventHook){if(UnhookWinEvent(eventHook))eventHook=nullptr;else closed=false;}if(hook){if(UnhookWindowsHookEx(hook))hook=nullptr;else closed=false;}return closed?1:0;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)module=instance;return TRUE;}
