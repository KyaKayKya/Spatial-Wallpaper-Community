#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <string>
#include <oleacc.h>
#pragma comment(lib,"oleacc.lib")
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"oleaut32.lib")
static uint8_t* trace;
static void Mark(int slot){LARGE_INTEGER q;QueryPerformanceCounter(&q);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16+slot*8),q.QuadPart);}
static bool Exact(HANDLE pipe,void* data,DWORD size){DWORD at=0;while(at<size){DWORD n=0;if(!ReadFile(pipe,static_cast<char*>(data)+at,size-at,&n,nullptr)||!n)return false;at+=n;}return true;}
int wmain(int argc,wchar_t** argv){
 if(argc!=2)return 2;
 wchar_t path[32768]{};GetModuleFileNameW(nullptr,path,32768);bool msaa=std::wstring(path).find(L"MsaaHelper.exe")!=std::wstring::npos;
 if(msaa&&FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 9;
 HANDLE pipe=CreateFileW(argv[1],GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);if(pipe==INVALID_HANDLE_VALUE)return 3;
 std::wstring name=std::wstring(argv[1]).substr(9)+L".trace";
 HANDLE mapping=OpenFileMappingW(FILE_MAP_WRITE,FALSE,name.c_str());if(!mapping)return 4;
 trace=static_cast<uint8_t*>(MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,256));if(!trace)return 5;
 LARGE_INTEGER frequency;QueryPerformanceFrequency(&frequency);memcpy(trace+144,&frequency.QuadPart,8);
 DWORD n=0;uint32_t hello[]={0x31484453,1,GetCurrentProcessId(),0};if(!WriteFile(pipe,hello,sizeof(hello),&n,nullptr)||n!=sizeof(hello))return 6;
 uint8_t request[32]{};
 while(Exact(pipe,request,sizeof(request))){
  InterlockedIncrement(reinterpret_cast<volatile LONG*>(trace));memcpy(trace+8,request+8,8);memset(trace+16,0,16*8);InterlockedIncrement(reinterpret_cast<volatile LONG*>(trace));
  Mark(0);uint64_t value=0;memcpy(&value,request+16,8);
  if(value){
   Mark(1);DWORD_PTR response=0;
   HWND window=reinterpret_cast<HWND>(static_cast<uintptr_t>(value));
   if(msaa){
    IAccessible* object=nullptr;HRESULT result=AccessibleObjectFromWindow(window,OBJID_CLIENT,IID_IAccessible,reinterpret_cast<void**>(&object));
    InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16+4*8),result);
    if(FAILED(result)||!object)return 10;
    object->Release();
   }else if(!SendMessageTimeoutW(window,WM_APP+777,0,0,SMTO_BLOCK|SMTO_ABORTIFHUNG,200,&response)||response!=0x5344)return 7;
   Mark(2);
  }
  uint8_t reply[48]{};memcpy(reply,hello,8);memcpy(reply+8,request+8,8);uint64_t now=GetTickCount64();memcpy(reply+16,&now,8);
  Mark(3);if(!WriteFile(pipe,reply,sizeof(reply),&n,nullptr)||n!=sizeof(reply))return 8;
 }
 UnmapViewOfFile(trace);CloseHandle(mapping);CloseHandle(pipe);if(msaa)CoUninitialize();return 0;
}
