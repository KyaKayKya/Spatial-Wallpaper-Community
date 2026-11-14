#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <string>
int wmain(int argc,wchar_t** argv){
 if(argc!=3)return 11;
 std::string result="{\"errors\":{";
 const wchar_t* deps[]={L"bcrypt.dll",L"WS2_32.dll",L"ole32.dll",L"OLEAUT32.dll",L"VERSION.dll",L"ADVAPI32.dll",L"WINMM.dll",L"KERNEL32.dll",L"USER32.dll",L"SHELL32.dll",argv[2]};
 for(int i=0;i<11;i++){
  SetLastError(0);HMODULE module=LoadLibraryW(deps[i]);DWORD error=module?0:GetLastError();
  if(i)result+=",";
  result+="\""+std::to_string(i)+"\":"+std::to_string(error);
  if(module)FreeLibrary(module);
 }
 result+="}}";
 HANDLE out=CreateFileW(argv[1],GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,0,nullptr);if(out==INVALID_HANDLE_VALUE)return 20;
 DWORD wrote=0;BOOL ok=WriteFile(out,result.data(),(DWORD)result.size(),&wrote,nullptr);CloseHandle(out);return ok?0:21;
}
