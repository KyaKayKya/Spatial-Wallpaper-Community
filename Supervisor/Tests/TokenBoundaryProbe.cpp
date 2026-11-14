// Original MIT OS boundary probe; no author execution.
#include <windows.h>
#include <cstdio>
#include <cstdlib>
int wmain(int argc,wchar_t** argv){
 if(argc!=4)return 2;HANDLE token=nullptr;if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return 3;
 DWORD size=0,ac=0,lpac=0;BOOL a=GetTokenInformation(token,TokenIsAppContainer,&ac,4,&size);DWORD ae=a?0:GetLastError();BOOL l=GetTokenInformation(token,TokenIsLessPrivilegedAppContainer,&lpac,4,&size);DWORD le=l?0:GetLastError();
 HANDLE pipe=CreateFileW(argv[1],GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);DWORD pe=pipe==INVALID_HANDLE_VALUE?GetLastError():0;if(pipe!=INVALID_HANDLE_VALUE)CloseHandle(pipe);
 HANDLE file=CreateFileW(argv[2],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);DWORD fe=file==INVALID_HANDLE_VALUE?GetLastError():0;if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);
 char bytes[384];int count=sprintf_s(bytes,"{\"app_container\":%lu,\"app_container_error\":%lu,\"lpac\":%lu,\"lpac_error\":%lu,\"manager_pipe_open_error\":%lu,\"endpoint_file_open_error\":%lu}",ac,ae,lpac,le,pe,fe);DWORD written=0;WriteFile((HANDLE)_wcstoui64(argv[3],nullptr,10),bytes,count,&written,nullptr);CloseHandle(token);return 0;
}
