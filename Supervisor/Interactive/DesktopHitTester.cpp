// Trusted, separately killable Explorer accessibility query. Never loads authors.
// The owner bounds every request and terminates this process if COM stalls.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <oleacc.h>
#include <cstdint>
#include <cstring>
#include <string>
#pragma comment(lib,"oleacc.lib")
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"oleaut32.lib")
struct Desktop { HWND list=nullptr; DWORD pid=0; unsigned matches=0; uint64_t created=0; };
// Fixed-size, owner-ACL telemetry survives helper termination. Stage numbers:
// 0 received, 1 Current done, 2 classification entered, 3 COM hit start,
// 4 COM hit done, 5 owner done, 6 role done, 7 release done,
// 8 classification done, 9 reply start, 10 reply done. Sequence 0 is warmup.
// 11 interactive-desktop check done, 14 shell process opened,
// 15 process identity read/handle closed; 15->1 includes desktop enumeration.
// Slots 12/13 retain warmup classification and whether COM was entered.
// Extra slots at byte 152/160 split object acquisition from accHitTest.
static uint8_t* trace=nullptr;
// Optional 96-byte identity/request sidecar. The parent seeds the pipe GUID at
// 32..47. Identity 0..31 publishes once; 48..79 is a bounded request commit.
static uint8_t* threadIdentity=nullptr;
static LONG64 Counter(){LARGE_INTEGER value{};QueryPerformanceCounter(&value);return value.QuadPart;}
static bool PublishThreadIdentity(){
 if(!threadIdentity)return false;FILETIME processCreated{},threadCreated{},exit{},kernel{},user{};
 if(!GetProcessTimes(GetCurrentProcess(),&processCreated,&exit,&kernel,&user)||!GetThreadTimes(GetCurrentThread(),&threadCreated,&exit,&kernel,&user))return false;
 InterlockedIncrement(reinterpret_cast<volatile LONG*>(threadIdentity));
 *reinterpret_cast<uint32_t*>(threadIdentity+4)=1;*reinterpret_cast<uint32_t*>(threadIdentity+8)=GetCurrentProcessId();*reinterpret_cast<uint32_t*>(threadIdentity+12)=GetCurrentThreadId();
 memcpy(threadIdentity+16,&processCreated,8);memcpy(threadIdentity+24,&threadCreated,8);
 InterlockedIncrement(reinterpret_cast<volatile LONG*>(threadIdentity));return true;
}
static void PublishThreadRequest(){
 if(!threadIdentity||!trace)return;
 InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(threadIdentity+48));
 InterlockedExchange64(reinterpret_cast<volatile LONG64*>(threadIdentity+56),*reinterpret_cast<volatile LONG64*>(trace+8));
 InterlockedExchange64(reinterpret_cast<volatile LONG64*>(threadIdentity+64),*reinterpret_cast<volatile LONG*>(trace));
 InterlockedExchange64(reinterpret_cast<volatile LONG64*>(threadIdentity+72),Counter());
 InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(threadIdentity+48));
}
static void Mark(unsigned stage){if(trace&&stage<16){InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16+stage*8),Counter());if(stage==0)PublishThreadRequest();}}
static void HitStage(unsigned stage){if(trace&&stage<2)InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+152+stage*8),Counter());}
// Separate seqlock for the current enumeration call. No names or window text.
// 1 callback entry, 2 DefView lookup done, 3 ListView lookup done,
// 4 PID lookup done, 5 visibility done, 6 enumeration returned.
static unsigned enumerated=0;
static void EnumStage(unsigned stage,HWND window){if(!trace)return;InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(trace+168));InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+176),stage);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+184),reinterpret_cast<LONG64>(window));InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+192),enumerated);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+200),Counter());InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(trace+168));}
// Last 48 bytes: seqlock, call site (1 Current / 2 post-hit), stage,
// outcome (0 pending / 1 true / 2 false), QPC, detail. Stages: 1 open input,
// 2 station handle, 3 station name, 4 thread desktop, 5 thread name,
// 6 input name, 7 close input, 8 function returned. Detail is Win32 error
// for a failed API, zero for a name mismatch, or failed stage at return.
static void InteractiveStage(unsigned site,unsigned stage,unsigned outcome,LONG64 detail=0){if(!trace)return;InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(trace+208));InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+216),site);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+224),stage);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+232),outcome);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+240),Counter());InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+248),detail);InterlockedIncrement64(reinterpret_cast<volatile LONG64*>(trace+208));}
static void Begin(uint64_t serial){if(!trace)return;InterlockedIncrement(reinterpret_cast<volatile LONG*>(trace));for(unsigned i=0;i<16;i++)if(i!=12&&i!=13)InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16+i*8),0);for(unsigned i=0;i<2;i++)InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+152+i*8),0);enumerated=0;EnumStage(0,nullptr);InteractiveStage(0,0,0);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+8),serial);InterlockedIncrement(reinterpret_cast<volatile LONG*>(trace));Mark(0);}
// This COM STA must dispatch messages even between requests. A blocking pipe
// read would otherwise leave accessibility callbacks waiting until the next
// input event. The owner still enforces the deadline and owns process teardown.
static bool Exact(HANDLE pipe,void* bytes,DWORD size,bool write){
 HANDLE event=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!event)return false;
 DWORD at=0;bool ok=true;while(at<size&&ok){OVERLAPPED operation{};operation.hEvent=event;ResetEvent(event);DWORD n=0;
  ok=(write?WriteFile(pipe,static_cast<char*>(bytes)+at,size-at,&n,&operation):ReadFile(pipe,static_cast<char*>(bytes)+at,size-at,&n,&operation))!=0;
  if(!ok&&GetLastError()==ERROR_IO_PENDING){
   for(;;){DWORD state=MsgWaitForMultipleObjectsEx(1,&event,INFINITE,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
    if(state==WAIT_OBJECT_0){ok=GetOverlappedResult(pipe,&operation,&n,FALSE)!=0;break;}
    if(state!=WAIT_OBJECT_0+1){CancelIoEx(pipe,&operation);GetOverlappedResult(pipe,&operation,&n,TRUE);ok=false;break;}
    MSG message;while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
   }
  }
  if(!n)ok=false;at+=n;
 }CloseHandle(event);return ok;
}
static bool Name(HANDLE value,const wchar_t* expected,unsigned site,unsigned stage){InteractiveStage(site,stage,0);wchar_t name[128]{};DWORD size=0;bool read=value&&GetUserObjectInformationW(value,UOI_NAME,name,sizeof(name),&size);DWORD error=value&&!read?GetLastError():0;bool ok=read&&!_wcsicmp(name,expected);InteractiveStage(site,stage,ok?1:2,error);return ok;}
static bool Interactive(unsigned site=1){
 InteractiveStage(site,1,0);HDESK input=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS);InteractiveStage(site,1,input?1:2,input?0:GetLastError());
 InteractiveStage(site,2,0);HWINSTA station=GetProcessWindowStation();InteractiveStage(site,2,station?1:2,station?0:GetLastError());
 bool ok=Name(station,L"WinSta0",site,3);unsigned failed=ok?0:station?3:2;
 if(ok){InteractiveStage(site,4,0);HDESK desktop=GetThreadDesktop(GetCurrentThreadId());InteractiveStage(site,4,desktop?1:2,desktop?0:GetLastError());ok=Name(desktop,L"Default",site,5);if(!ok)failed=desktop?5:4;}
 if(ok){ok=Name(input,L"Default",site,6);if(!ok)failed=input?6:1;}
 if(input){InteractiveStage(site,7,0);BOOL closed=CloseDesktop(input);InteractiveStage(site,7,closed?1:2,closed?0:GetLastError());}
 InteractiveStage(site,8,ok?1:2,failed);return ok;
}
static BOOL CALLBACK Find(HWND window,LPARAM value){auto& desktop=*reinterpret_cast<Desktop*>(value);++enumerated;EnumStage(1,window);HWND view=FindWindowExW(window,nullptr,L"SHELLDLL_DefView",nullptr);EnumStage(2,window);HWND list=view?FindWindowExW(view,nullptr,L"SysListView32",nullptr):nullptr;EnumStage(3,window);DWORD pid=0;GetWindowThreadProcessId(list,&pid);EnumStage(4,window);bool visible=list&&IsWindowVisible(list);EnumStage(5,window);if(visible&&pid==desktop.pid){desktop.list=list;desktop.matches++;}return TRUE;}
static Desktop Current(){Desktop desktop;if(!Interactive())return desktop;Mark(11);GetWindowThreadProcessId(GetShellWindow(),&desktop.pid);if(!desktop.pid)return desktop;HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,desktop.pid);Mark(14);FILETIME created{},exit{},kernel{},user{};bool valid=process&&GetProcessTimes(process,&created,&exit,&kernel,&user);if(process)CloseHandle(process);Mark(15);if(!valid){desktop.pid=0;return desktop;}desktop.created=(uint64_t(created.dwHighDateTime)<<32)|created.dwLowDateTime;bool found=EnumWindows(Find,reinterpret_cast<LPARAM>(&desktop))!=0;EnumStage(6,nullptr);if(!found||desktop.matches!=1){desktop.list=nullptr;}return desktop;}
// 0 unavailable, 1 visible blank, 2 icon, 3 covered, 4 unknown. No names/content.
static uint32_t Classify(POINT point,const Desktop& desktop){
 Mark(2);
 if(!desktop.list)return 0;
 if(WindowFromPoint(point)!=desktop.list)return 3;
 IAccessible* object=nullptr;VARIANT child{},role{};HWND owner=nullptr;
 // Query only the independently verified visible desktop ListView. Do not cache
 // the COM object or a prior blank result across requests/window generations.
 Mark(3);HRESULT a=AccessibleObjectFromWindow(desktop.list,OBJID_CLIENT,IID_IAccessible,reinterpret_cast<void**>(&object));
 HitStage(0);if(SUCCEEDED(a)&&object){HitStage(1);a=object->accHitTest(point.x,point.y,&child);}Mark(4);
 HRESULT b=SUCCEEDED(a)&&object&&child.vt==VT_I4?WindowFromAccessibleObject(object,&owner):E_FAIL;Mark(5);
 HRESULT c=SUCCEEDED(b)&&owner==desktop.list?object->get_accRole(child,&role):E_FAIL;Mark(6);
 uint32_t kind=4;if(SUCCEEDED(a)&&SUCCEEDED(b)&&SUCCEEDED(c)&&owner==desktop.list&&child.vt==VT_I4&&role.vt==VT_I4){if(child.lVal==CHILDID_SELF&&role.lVal==ROLE_SYSTEM_LIST)kind=1;else if(child.lVal>CHILDID_SELF&&role.lVal==ROLE_SYSTEM_LISTITEM)kind=2;}
 VariantClear(&child);VariantClear(&role);if(object)object->Release();Mark(7);
 DWORD pid=0;GetWindowThreadProcessId(desktop.list,&pid);if(!Interactive(2)||pid!=desktop.pid||!IsWindowVisible(desktop.list)||WindowFromPoint(point)!=desktop.list)return 4;return kind;
}
int wmain(int argc,wchar_t** argv){
 if(argc!=2)return 2;SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
 HANDLE pipe=CreateFileW(argv[1],GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_FLAG_OVERLAPPED,nullptr);if(pipe==INVALID_HANDLE_VALUE)return 3;
 if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED))){CloseHandle(pipe);return 4;}
 std::wstring pipeName=argv[1];HANDLE mapping=nullptr;if(pipeName.rfind(L"\\\\.\\pipe\\",0)==0){auto traceName=pipeName.substr(9)+L".trace";mapping=OpenFileMappingW(FILE_MAP_WRITE,FALSE,traceName.c_str());if(mapping)trace=static_cast<uint8_t*>(MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,256));}
 HANDLE identityMapping=nullptr;if(pipeName.rfind(L"\\\\.\\pipe\\",0)==0){auto identityName=pipeName.substr(9)+L".thread";identityMapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,identityName.c_str());if(identityMapping)threadIdentity=static_cast<uint8_t*>(MapViewOfFile(identityMapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,96));}
 PublishThreadIdentity();
 if(trace){LARGE_INTEGER frequency{};QueryPerformanceFrequency(&frequency);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+144),frequency.QuadPart);}
 // Warm COM outside the input callback's request budget; startup is bounded too.
 Begin(0);Desktop initial=Current();Mark(1);POINT point{};GetCursorPos(&point);uint32_t warmup=Classify(point,initial);Mark(8);if(trace){InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16+12*8),warmup);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16+13*8),*reinterpret_cast<volatile LONG64*>(trace+16+3*8)!=0?1:0);}
 uint32_t hello[]={0x31484453,1,GetCurrentProcessId(),0};bool ok=Exact(pipe,hello,sizeof(hello),true);uint64_t previous=0;
 uint8_t request[32]{};
 while(ok&&Exact(pipe,request,sizeof(request),false)){
  uint32_t magic=0,version=0;uint64_t serial=0,reserved=0;memcpy(&magic,request,4);memcpy(&version,request+4,4);memcpy(&serial,request+8,8);memcpy(&point.x,request+16,4);memcpy(&point.y,request+20,4);memcpy(&reserved,request+24,8);
  if(magic!=0x31484453||version!=1||serial!=previous+1||reserved)break;previous=serial;
  uint64_t at=GetTickCount64();Begin(serial);Desktop desktop=Current();Mark(1);uint32_t kind=Classify(point,desktop);Mark(8);uint8_t reply[48]{};memcpy(reply,hello,8);memcpy(reply+8,&serial,8);memcpy(reply+16,&at,8);
  if(kind){uint64_t window=reinterpret_cast<uint64_t>(desktop.list);memcpy(reply+24,&window,8);memcpy(reply+32,&desktop.created,8);memcpy(reply+40,&desktop.pid,4);memcpy(reply+44,&kind,4);}Mark(9);ok=Exact(pipe,reply,sizeof(reply),true);Mark(10);
 }
 CoUninitialize();if(threadIdentity)UnmapViewOfFile(threadIdentity);if(identityMapping)CloseHandle(identityMapping);if(trace)UnmapViewOfFile(trace);if(mapping)CloseHandle(mapping);CloseHandle(pipe);return ok?0:5;
}
