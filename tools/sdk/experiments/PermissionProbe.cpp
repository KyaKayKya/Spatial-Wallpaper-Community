// Native direct-OS probe. Not distributed as an author package.
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <windows.h>
#include <cstdio>
#include <string>
void phase(const wchar_t* path,const char* text){std::wstring p=std::wstring(path)+L".phase";HANDLE h=CreateFileW(p.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,0,nullptr);if(h!=INVALID_HANDLE_VALUE){DWORD n;WriteFile(h,text,(DWORD)strlen(text),&n,nullptr);CloseHandle(h);}}
int wmain(int argc,wchar_t** argv) {
 SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX);
 if(argc==2 && std::wstring(argv[1])==L"--child")return 99;
 if(argc!=5&&argc!=6)return 11;
 phase(argv[1],"token");
 HANDLE token=nullptr;DWORD app=0,needed=0;
 if(!OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token))return 12;
 if(!GetTokenInformation(token,TokenIsAppContainer,&app,sizeof(app),&needed))return 13;
 CloseHandle(token);
 phase(argv[1],"file");
 HANDLE file=CreateFileW(argv[2],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);
 bool read=file!=INVALID_HANDLE_VALUE;if(read)CloseHandle(file);
 phase(argv[1],"network");
 WSADATA data;WSAStartup(MAKEWORD(2,2),&data);
 SOCKET sock=socket(AF_INET,SOCK_STREAM,IPPROTO_TCP);
 sockaddr_in address={};address.sin_family=AF_INET;address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);address.sin_port=htons((u_short)_wtoi(argv[3]));
 u_long nonblock=1;ioctlsocket(sock,FIONBIO,&nonblock);
 bool connected=connect(sock,(sockaddr*)&address,sizeof(address))==0;
 if(!connected && WSAGetLastError()==WSAEWOULDBLOCK){
  fd_set writers,errors;FD_ZERO(&writers);FD_ZERO(&errors);FD_SET(sock,&writers);FD_SET(sock,&errors);timeval timeout={1,500000};
  if(select(0,nullptr,&writers,&errors,&timeout)>0 && FD_ISSET(sock,&writers)){
   int error=0,size=sizeof(error);connected=getsockopt(sock,SOL_SOCKET,SO_ERROR,(char*)&error,&size)==0 && error==0;
  }
 }
 closesocket(sock);WSACleanup();
 phase(argv[1],"spawn");
 STARTUPINFOW si={sizeof(si)};PROCESS_INFORMATION pi={};
 std::wstring command=L"\""+std::wstring(argv[0])+L"\" --child";
 bool spawn=CreateProcessW(argv[0],&command[0],nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&si,&pi)!=FALSE;
 if(spawn){WaitForSingleObject(pi.hProcess,2000);TerminateProcess(pi.hProcess,99);CloseHandle(pi.hThread);CloseHandle(pi.hProcess);}
 phase(argv[1],"input");
 bool before=(GetAsyncKeyState(VK_F24)&0x8000)!=0;
 SetLastError(0);HDESK input=OpenInputDesktop(0,FALSE,DESKTOP_READOBJECTS|DESKTOP_WRITEOBJECTS);
 bool opened=input!=nullptr;DWORD openError=opened?0:GetLastError();
 SetLastError(0);bool attached=input && SetThreadDesktop(input);DWORD attachError=(input&&!attached)?GetLastError():0;
 bool after=(GetAsyncKeyState(VK_F24)&0x8000)!=0;
 if(input)CloseDesktop(input);
 phase(argv[1],"station");
 HWINSTA interactive=OpenWindowStationW(L"WinSta0",FALSE,WINSTA_ENUMDESKTOPS|WINSTA_READATTRIBUTES);
 bool escaped=interactive && SetProcessWindowStation(interactive);
 if(interactive)CloseWindowStation(interactive);
 HANDLE foreign=OpenProcess(PROCESS_VM_READ|PROCESS_DUP_HANDLE,FALSE,(DWORD)_wtoi(argv[4]));
 bool foreignAccess=foreign!=nullptr;if(foreign)CloseHandle(foreign);
 bool inheritedSecret=getenv("SPATIAL_TEST_PARENT_SECRET")!=nullptr;
 phase(argv[1],"result");
 char json[1024];int count=snprintf(json,sizeof(json),"{\"app_container\":%lu,\"outside_file_read\":%s,\"loopback_connect\":%s,\"spawn_child\":%s,\"input_desktop_opened\":%s,\"input_desktop_open_error\":%lu,\"input_desktop_attached\":%s,\"input_desktop_attach_error\":%lu,\"f24_marker\":%s,\"f24_after_switch\":%s,\"escaped_station\":%s,\"foreign_process_access\":%s,\"inherited_parent_secret\":%s}",app,read?"true":"false",connected?"true":"false",spawn?"true":"false",opened?"true":"false",openError,attached?"true":"false",attachError,before?"true":"false",after?"true":"false",escaped?"true":"false",foreignAccess?"true":"false",inheritedSecret?"true":"false");
 HANDLE out=argc==6?(HANDLE)_wcstoui64(argv[5],nullptr,10):CreateFileW(argv[1],GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
 if(out==INVALID_HANDLE_VALUE)return 20;
 DWORD wrote=0;bool ok=WriteFile(out,json,count,&wrote,nullptr)!=FALSE && wrote==(DWORD)count;CloseHandle(out);return ok?0:21;
}
