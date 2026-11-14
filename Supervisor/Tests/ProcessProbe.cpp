#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <cstdint>
#include <cstdlib>
struct Packet{uint32_t magic,kind;int64_t sequence;};
int wmain(int argc,wchar_t** argv){
 if(argc==2&&std::wstring(argv[1])==L"--exit")return 0;
 if(argc!=3)return 11;
 std::wstring mode=argv[1];HANDLE pipe=(HANDLE)(uintptr_t)_wcstoui64(argv[2],nullptr,10);
 auto send=[&](uint32_t kind,int64_t sequence){Packet value={0x53575331,kind,sequence};DWORD count=0;return WriteFile(pipe,&value,sizeof(value),&count,nullptr)&&count==sizeof(value);};
 if(mode==L"--startup-heartbeat"){while(send(2,0))Sleep(20);return 0;}
 if(!send(1,1))return 12;
 if(mode==L"--blocked"){Sleep(INFINITE);return 13;}
 if(mode==L"--cpu"){volatile uint64_t counter=0;while(true)counter++;return 14;}
 int64_t sequence=1;
 while(true){
  if(mode==L"--memory"){
   BYTE* memory=(BYTE*)VirtualAlloc(nullptr,4*1024*1024,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);if(!memory)return 15;
   for(int i=0;i<4*1024*1024;i+=4096)memory[i]=1;
  }
  if(!send(mode==L"--heartbeat"?2:1,mode==L"--heartbeat"?0:++sequence))return 0;
  Sleep(20);
 }
}
