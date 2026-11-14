#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
int wmain(int argc,wchar_t** argv){
 if(argc!=3&&argc!=4)return 11;
 std::string json="{";
 auto add=[&](const char* key,DWORD value){if(json.size()>1)json+=",";json+="\"";json+=key;json+="\":";json+=std::to_string(value);};
 HANDLE file=CreateFileW(argv[2],GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
 add("profile_file_write",file==INVALID_HANDLE_VALUE?GetLastError():0);
 if(file!=INVALID_HANDLE_VALUE){DWORD n;WriteFile(file,"SDK synthetic canary",20,&n,nullptr);CloseHandle(file);}
 HMODULE userenv=LoadLibraryW(L"userenv.dll");add("load_userenv",userenv?0:GetLastError());
 if(userenv){
  typedef HRESULT(WINAPI* GetLocation)(REGSAM,PHKEY);
  auto get=(GetLocation)GetProcAddress(userenv,"GetAppContainerRegistryLocation");HKEY key=nullptr;
  HRESULT hr=get?get(KEY_READ,&key):E_NOTIMPL;add("registry_location_hr",(DWORD)hr);
  if(SUCCEEDED(hr)){
   HKEY writable=nullptr;LONG writeOpen=RegOpenKeyExW(key,L"",0,KEY_SET_VALUE,&writable);add("private_registry_write_open",writeOpen);
   DWORD value=1;add("private_registry_write",writeOpen?writeOpen:RegSetValueExW(writable,L"SdkSyntheticCanary",0,REG_DWORD,(BYTE*)&value,sizeof(value)));
   if(writable)RegCloseKey(writable);
   HKEY creator=nullptr;LONG createOpen=RegOpenKeyExW(key,L"",0,KEY_CREATE_SUB_KEY,&creator);add("private_registry_create_open",createOpen);
   HKEY sub=nullptr;LONG create=createOpen?createOpen:RegCreateKeyExW(creator,L"SdkSyntheticSubkey",0,nullptr,0,KEY_READ|KEY_WRITE,nullptr,&sub,nullptr);add("private_registry_create",create);if(sub)RegCloseKey(sub);if(creator)RegCloseKey(creator);
   HKEY permissions=nullptr;LONG change=RegOpenKeyExW(key,L"",0,WRITE_DAC|WRITE_OWNER,&permissions);add("private_registry_acl",change);if(permissions)RegCloseKey(permissions);
   typedef LONG(WINAPI* QueryKey)(HANDLE,int,void*,ULONG,ULONG*);
   auto query=(QueryKey)GetProcAddress(GetModuleHandleW(L"ntdll.dll"),"NtQueryKey");
   BYTE buffer[4096]={};ULONG needed;
   if(query&&query(key,3,buffer,sizeof(buffer),&needed)>=0){
    std::wstring name((wchar_t*)(buffer+4),*(DWORD*)buffer/2);
    std::wstring marker(1,(wchar_t)92);marker+=L"Local Settings";marker+=(wchar_t)92;
    auto index=name.find(marker);
    if(index!=std::wstring::npos){
     std::string suffix;for(wchar_t c:name.substr(index)){if(c==92){suffix+=(char)92;suffix+=(char)92;}else if(c>=32&&c<127)suffix+=(char)c;}
     json+=",\"registry_suffix\":\""+suffix+"\"";
    }
   }
   RegCloseKey(key);
  }
  FreeLibrary(userenv);
 }
 json+="}";
 HANDLE output=argc==4?(HANDLE)_wcstoui64(argv[3],nullptr,10):CreateFileW(argv[1],GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,0,nullptr);if(output==INVALID_HANDLE_VALUE)return 20;DWORD n;BOOL ok=WriteFile(output,json.data(),(DWORD)json.size(),&n,nullptr);CloseHandle(output);return ok?0:21;
}
