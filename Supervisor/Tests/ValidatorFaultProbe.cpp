#define WIN32_LEAN_AND_MEAN
#include <windows.h>
// Original MIT fault fixture; never included in a production Validator image.
int wmain(int argc,wchar_t** argv){
 if(argc!=5)return 2;
 HANDLE source=CreateFileW(argv[1],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);char mode=0;DWORD count=0;
 if(source==INVALID_HANDLE_VALUE||!ReadFile(source,&mode,1,&count,nullptr))return 3;CloseHandle(source);
 if(mode=='H'){Sleep(INFINITE);return 4;}
 char bytes[8193]={};DWORD written=0;return WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),bytes,sizeof(bytes),&written,nullptr)&&written==sizeof(bytes)?0:5;
}
