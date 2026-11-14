
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wincred.h>
#include <cstdio>
#include <string>
int wmain(int argc,wchar_t** argv){
 if(argc==3){
  if(std::wstring(argv[1])==L"--seed"){BYTE blob[]={1,2,3,4};CREDENTIALW value={};value.Type=CRED_TYPE_GENERIC;value.TargetName=argv[2];value.CredentialBlobSize=4;value.CredentialBlob=blob;value.Persist=CRED_PERSIST_LOCAL_MACHINE;value.UserName=(LPWSTR)L"SDK-Fake-Canary";return CredWriteW(&value,0)?0:GetLastError();}
  if(std::wstring(argv[1])==L"--cleanup")return CredDeleteW(argv[2],CRED_TYPE_GENERIC,0)?0:GetLastError();
 }
 if(argc!=10)return 11;
 std::string result="{";
 auto add=[&](const char* name,DWORD error){if(result.size()>1)result+=char(44);result+=char(34);result+=name;result+=char(34);result+=char(58);result+=std::to_string(error);};
 for(int i=2;i<=3;i++){HANDLE file=CreateFileW(argv[i],GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);add(i==2?"private_file":"all_apps_file",file==INVALID_HANDLE_VALUE?GetLastError():0);if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);}
 for(int i=4;i<=5;i++){HKEY key=nullptr;LONG error=RegOpenKeyExW(HKEY_CURRENT_USER,argv[i],0,KEY_READ,&key);add(i==4?"inherited_private_registry":"protected_private_registry",error);if(key)RegCloseKey(key);}
 HKEY system=nullptr;LONG error=RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",0,KEY_READ,&system);add("operational_system_registry",error);if(system)RegCloseKey(system);
 for(int i=6;i<=8;i++){HMODULE module=LoadLibraryExW(argv[i],nullptr,0);add(i==6?"external_dll":i==7?"scratch_dll":"trusted_dll",module?0:GetLastError());if(module)FreeLibrary(module);}
 PCREDENTIALW credential=nullptr;BOOL ok=CredReadW(argv[9],CRED_TYPE_GENERIC,0,&credential);add("private_credential",ok?0:GetLastError());if(credential)CredFree(credential);
 result+="}";
 HANDLE out=CreateFileW(argv[1],GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,0,nullptr);if(out==INVALID_HANDLE_VALUE)return 20;DWORD wrote;ok=WriteFile(out,result.data(),(DWORD)result.size(),&wrote,nullptr);CloseHandle(out);return ok?0:21;
}
