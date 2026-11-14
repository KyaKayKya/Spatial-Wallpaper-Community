#include <windows.h>
#include <oleacc.h>
#include <thread>
#include <iostream>
static int failure=0;static bool closeOk=true;static int closedCount=0;static HANDLE entered=nullptr,resume=nullptr;static int pauseStage=0;
static void Pause(int stage){if(pauseStage==stage){SetEvent(entered);WaitForSingleObject(resume,5000);}}
static HDESK FakeOpen(DWORD,BOOL,ACCESS_MASK){Pause(1);if(failure==1){SetLastError(5);return nullptr;}return reinterpret_cast<HDESK>(3);}
static HWINSTA FakeStation(){Pause(2);return reinterpret_cast<HWINSTA>(1);}
static HDESK FakeThreadDesktop(DWORD){Pause(4);return reinterpret_cast<HDESK>(2);}
static DWORD FakeThreadId(){return 77;}
static BOOL FakeName(HANDLE value,int,void* output,DWORD length,DWORD* needed){int id=static_cast<int>(reinterpret_cast<INT_PTR>(value));Pause(id==1?3:id==2?5:6);if(failure==5&&id==1){SetLastError(5);return FALSE;}const wchar_t* name=failure==id+1?L"Other":id==1?L"WinSta0":L"Default";*needed=static_cast<DWORD>((wcslen(name)+1)*sizeof(wchar_t));if(length<*needed)return FALSE;memcpy(output,name,*needed);return TRUE;}
static BOOL FakeClose(HDESK){Pause(7);++closedCount;return closeOk;}
#define OpenInputDesktop FakeOpen
#define GetProcessWindowStation FakeStation
#define GetThreadDesktop FakeThreadDesktop
#define GetCurrentThreadId FakeThreadId
#define GetUserObjectInformationW FakeName
#define CloseDesktop FakeClose
#define wmain DesktopHitProductionMain
#include "../Interactive/DesktopHitTester.cpp"
#undef wmain
#undef OpenInputDesktop
#undef GetProcessWindowStation
#undef GetThreadDesktop
#undef GetCurrentThreadId
#undef GetUserObjectInformationW
#undef CloseDesktop
static void Need(bool ok,const char* reason){if(!ok)throw std::runtime_error(reason);std::cout<<"PASS "<<reason<<std::endl;}
int wmain(){
 try{
 alignas(8) uint8_t bytes[264]{};memset(bytes+256,0xa5,8);trace=bytes;
 Begin(41);enumerated=17;EnumStage(3,reinterpret_cast<HWND>(123));
 Begin(42); // Deliberately do not enter Current: old query must be gone already.
 auto get=[&](size_t at){return *reinterpret_cast<LONG64*>(bytes+at);};
 bool ok=get(8)==42&&get(176)==0&&get(184)==0&&get(192)==0&&get(200)>0&&(get(168)&1)==0;
 Need(ok,"new-request-clears-prior-enumeration-before-Current");
 Need(Interactive(),"mock-interactive-success");
 Need(get(216)==1&&get(224)==8&&get(232)==1&&get(240)>0&&get(248)==0&&(get(208)&1)==0,"interactive-success-location-stage-outcome");
 for(int mode=1;mode<=5;mode++){failure=mode;closedCount=0;bool returned=Interactive();int failed=mode==1?1:mode==2||mode==5?3:mode==3?5:6;Need(!returned&&get(224)==8&&get(232)==2&&get(248)==failed,"false-return-identifies-failing-check");Need(closedCount==(mode==1?0:1),"false-return-preserves-input-desktop-close");}
 failure=1;auto unavailable=Current();Need(!unavailable.list&&!unavailable.pid,"actual-false-Current-returns-unavailable-without-enumeration");
 failure=0;closeOk=false;Need(Interactive()&&get(232)==1,"close-failure-does-not-change-existing-return-semantics");closeOk=true;
 Need(Interactive(2)&&get(216)==2&&get(224)==8&&get(232)==1,"post-hit-site-distinct-from-Current");
 entered=CreateEventW(nullptr,TRUE,FALSE,nullptr);resume=CreateEventW(nullptr,TRUE,FALSE,nullptr);Need(entered&&resume,"controlled-wait-events-created");
 for(int stage=1;stage<=7;stage++){
  Begin(100+stage);pauseStage=stage;ResetEvent(entered);ResetEvent(resume);bool returned=false;std::thread worker([&]{returned=Interactive(2);});DWORD signaled=WaitForSingleObject(entered,2000);auto site=get(216),phase=get(224),outcome=get(232),stamp=get(240),sequence=get(208);Sleep(30);bool unchanged=get(240)==stamp;SetEvent(resume);worker.join();
  Need(signaled==WAIT_OBJECT_0&&site==2&&phase==stage&&outcome==0&&stamp>0&&(sequence&1)==0&&unchanged,"controlled-call-wait-remains-pending-at-exact-stage");Need(returned&&get(224)==8&&get(232)==1,"released-call-completes-without-altering-outcome");
 }
 pauseStage=0;CloseHandle(entered);CloseHandle(resume);entered=resume=nullptr;
 Begin(200);Need(get(216)==0&&get(224)==0&&get(232)==0&&get(240)>0&&get(248)==0&&(get(208)&1)==0,"new-request-clears-prior-interactive-before-Current");
 for(int i=256;i<264;i++)Need(bytes[i]==0xa5,"interactive-trace-stays-within-256-bytes");
 trace=nullptr;Need(Interactive(),"disabled-telemetry-preserves-return");
 trace=nullptr;return 0;
 }catch(const std::exception& error){std::cerr<<"FAIL "<<error.what()<<std::endl;return 1;}
}
