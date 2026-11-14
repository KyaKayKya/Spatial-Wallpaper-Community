#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <string>
static uint8_t* trace=nullptr;
static void Trace(const uint8_t* request){if(!trace)return;InterlockedIncrement(reinterpret_cast<volatile LONG*>(trace));uint64_t serial=0;memcpy(&serial,request+8,8);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+8),serial);InterlockedExchange64(reinterpret_cast<volatile LONG64*>(trace+16),static_cast<LONG64>(GetTickCount64()));InterlockedIncrement(reinterpret_cast<volatile LONG*>(trace));}
static bool Exact(HANDLE pipe,void* data,DWORD size){DWORD at=0;while(at<size){DWORD n=0;if(!ReadFile(pipe,static_cast<char*>(data)+at,size-at,&n,nullptr)||!n)return false;at+=n;}return true;}
int wmain(int argc,wchar_t** argv){if(argc!=2)return 2;char path[32768]{};GetModuleFileNameA(nullptr,path,sizeof(path));HANDLE input=CreateFileW(argv[1],GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);if(input==INVALID_HANDLE_VALUE)return 3;std::wstring name=std::wstring(argv[1]).substr(9)+L".trace";HANDLE mapping=OpenFileMappingW(FILE_MAP_WRITE,FALSE,name.c_str());if(mapping)trace=static_cast<uint8_t*>(MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,256));HANDLE output=input;DWORD n=0;uint32_t hello[]={0x31484453,1,GetCurrentProcessId(),0};WriteFile(output,hello,sizeof(hello),&n,nullptr);
 uint8_t request[32]{};while(Exact(input,request,sizeof(request))){Trace(request);if(strstr(path,"Hang")){Sleep(10000);return 0;}uint8_t reply[48]{};memcpy(reply,hello,8);memcpy(reply+8,request+8,8);uint64_t now=GetTickCount64();if(strstr(path,"Stale"))now-=1000;memcpy(reply+16,&now,8);if(strstr(path,"Invalid")){uint32_t bad=99;memcpy(reply+44,&bad,4);}if(strstr(path,"Partial")){WriteFile(output,reply,8,&n,nullptr);Sleep(10000);return 0;}WriteFile(output,reply,sizeof(reply),&n,nullptr);}return 0;}
