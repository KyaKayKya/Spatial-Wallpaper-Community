// Uses only identity/counter publication. Never calls helper main or desktop APIs.
#define wmain UnusedDesktopHelperMain
#include "../Interactive/DesktopHitTester.cpp"
#undef wmain
#include <iostream>
static void Need(bool value,const char* name){if(!value)throw name;std::cout<<"PASS "<<name<<"\n";}
int wmain(int argc,wchar_t** argv){if(argc==2){
 // Protocol-only helper: publish on the pipe's actual servicing thread, without
 // Current/Classify/COM/foreground/window queries or input operations.
 HANDLE pipe=CreateFileW(argv[1],GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,0,nullptr);if(pipe==INVALID_HANDLE_VALUE)return 10;
 std::wstring name=std::wstring(argv[1]).substr(9);HANDLE id=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,(name+L".thread").c_str()),st=OpenFileMappingW(FILE_MAP_WRITE,FALSE,(name+L".trace").c_str());if(!st){std::cerr<<"mapping-open:"<<GetLastError()<<"\n";return 11;}
 if(id)threadIdentity=static_cast<uint8_t*>(MapViewOfFile(id,FILE_MAP_READ|FILE_MAP_WRITE,0,0,96));trace=static_cast<uint8_t*>(MapViewOfFile(st,FILE_MAP_WRITE,0,0,256));if(!trace){std::cerr<<"mapping-view:"<<GetLastError()<<"\n";return 12;}PublishThreadIdentity();
 Begin(0);uint32_t hello[]={0x31484453,1,GetCurrentProcessId(),0};DWORD count=0;if(!WriteFile(pipe,hello,sizeof(hello),&count,nullptr)||count!=sizeof(hello))return 13;
 uint8_t request[32]{};while(ReadFile(pipe,request,32,&count,nullptr)&&count==32){uint64_t serial=0;memcpy(&serial,request+8,8);Begin(serial);uint8_t reply[48]{};memcpy(reply,request,16);uint64_t now=GetTickCount64();memcpy(reply+16,&now,8);if(!WriteFile(pipe,reply,48,&count,nullptr)||count!=48)break;}return 0;
 }try{
 alignas(8) uint8_t identity[104]{},stages[264]{};threadIdentity=identity;trace=stages;
 memset(identity+96,0x5a,8);memset(stages+256,0x5a,8);for(int i=32;i<48;i++)identity[i]=static_cast<uint8_t>(i);
 Need(PublishThreadIdentity(),"native-thread-identity-published");
 Need(*reinterpret_cast<LONG*>(identity)==2&&*reinterpret_cast<uint32_t*>(identity+4)==1,"native-identity-schema-committed");
 Need(*reinterpret_cast<uint32_t*>(identity+8)==GetCurrentProcessId()&&*reinterpret_cast<uint32_t*>(identity+12)==GetCurrentThreadId(),"native-publisher-actual-thread");
 FILETIME created{},exit{},kernel{},user{};Need(GetThreadTimes(GetCurrentThread(),&created,&exit,&kernel,&user)&&memcmp(identity+24,&created,8)==0,"native-thread-creation-exact");
 Need(GetProcessTimes(GetCurrentProcess(),&created,&exit,&kernel,&user)&&memcmp(identity+16,&created,8)==0,"native-process-creation-exact");
 for(int i=32;i<48;i++)Need(identity[i]==i,"parent-generation-preserved");
 Begin(17);Need(*reinterpret_cast<LONG64*>(identity+48)==2&&*reinterpret_cast<LONG64*>(identity+56)==17&&*reinterpret_cast<LONG64*>(identity+64)==*reinterpret_cast<LONG*>(trace),"request-commit-matches-trace");
 Begin(18);Need(*reinterpret_cast<LONG64*>(identity+48)==4&&*reinterpret_cast<LONG64*>(identity+56)==18,"request-commit-replaced-bounded");
 for(int i=0;i<8;i++)Need(identity[96+i]==0x5a&&stages[256+i]==0x5a,"fixed-layout-guard-preserved");
 threadIdentity=nullptr;trace=nullptr;return 0;
 }catch(const char* error){std::cerr<<error<<"\n";return 1;}}
