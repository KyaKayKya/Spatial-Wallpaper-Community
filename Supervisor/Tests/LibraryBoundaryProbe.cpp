#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <string>
int wmain(int argc,wchar_t** argv){
 if(argc!=3)return 11;
 std::string result="{";
 auto add=[&](const char* name,DWORD error){if(result.size()>1)result+=char(44);result+=char(34);result+=name;result+=char(34);result+=char(58);result+=std::to_string(error);};
 HANDLE file=CreateFileW(argv[2],GENERIC_READ|GENERIC_EXECUTE,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);add("open_read_execute",file==INVALID_HANDLE_VALUE?GetLastError():0);
 if(file!=INVALID_HANDLE_VALUE){
  HANDLE section=CreateFileMappingW(file,nullptr,PAGE_READONLY|SEC_IMAGE,0,0,nullptr);add("image_section",section?0:GetLastError());if(section)CloseHandle(section);CloseHandle(file);
 }
 for(DWORD flags:{1u,0u,0x900u}){
  SetLastError(0);HMODULE module=LoadLibraryExW(argv[2],nullptr,flags);std::string name="load_"+std::to_string(flags);add(name.c_str(),module?0:GetLastError());if(module)FreeLibrary(module);
 }
 result+="}";
 HANDLE out=CreateFileW(argv[1],GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,0,nullptr);if(out==INVALID_HANDLE_VALUE)return 20;DWORD wrote;BOOL ok=WriteFile(out,result.data(),(DWORD)result.size(),&wrote,nullptr);CloseHandle(out);return ok?0:21;
}
