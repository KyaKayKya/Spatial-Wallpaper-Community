// Includes the actual sampler implementation. Only activation is an API
// substitute: no device is activated, while Capture's real 2000ms wait runs.
#define NOMINMAX
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>
#include <cstdio>
#include <stdexcept>
#include <thread>
#include <vector>
HRESULT WINAPI ControlledActivate(LPCWSTR,REFIID,PROPVARIANT*,IActivateAudioInterfaceCompletionHandler*,IActivateAudioInterfaceAsyncOperation**);
DWORD WINAPI ObservedWait(HANDLE,DWORD);
#define ActivateAudioInterfaceAsync ControlledActivate
#define WaitForSingleObject ObservedWait
#define main ProductionSamplerMain
#include "../Interactive/ProcessAudioSampler.cpp"
#undef main
#undef ActivateAudioInterfaceAsync
#undef WaitForSingleObject

static int clients=0,operations=0,initializations=0,resultCalls=0;
static void Need(bool value,const char* reason){if(!value)throw std::runtime_error(reason);}
class FakeClient final:public IAudioClient {
 LONG refs=1;
public:
 FakeClient(){++clients;}~FakeClient(){--clients;}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** value)override{if(!value)return E_POINTER;*value=nullptr;if(iid!=__uuidof(IUnknown)&&iid!=__uuidof(IAudioClient))return E_NOINTERFACE;*value=static_cast<IAudioClient*>(this);AddRef();return S_OK;}
 ULONG STDMETHODCALLTYPE AddRef()override{return InterlockedIncrement(&refs);}ULONG STDMETHODCALLTYPE Release()override{ULONG n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
 HRESULT STDMETHODCALLTYPE Initialize(AUDCLNT_SHAREMODE,DWORD,REFERENCE_TIME,REFERENCE_TIME,const WAVEFORMATEX*,LPCGUID)override{++initializations;return E_FAIL;}
 HRESULT STDMETHODCALLTYPE GetBufferSize(UINT32*)override{return E_NOTIMPL;}HRESULT STDMETHODCALLTYPE GetStreamLatency(REFERENCE_TIME*)override{return E_NOTIMPL;}
 HRESULT STDMETHODCALLTYPE GetCurrentPadding(UINT32*)override{return E_NOTIMPL;}HRESULT STDMETHODCALLTYPE IsFormatSupported(AUDCLNT_SHAREMODE,const WAVEFORMATEX*,WAVEFORMATEX**)override{return E_NOTIMPL;}
 HRESULT STDMETHODCALLTYPE GetMixFormat(WAVEFORMATEX**)override{return E_NOTIMPL;}HRESULT STDMETHODCALLTYPE GetDevicePeriod(REFERENCE_TIME*,REFERENCE_TIME*)override{return E_NOTIMPL;}
 HRESULT STDMETHODCALLTYPE Start()override{return E_NOTIMPL;}HRESULT STDMETHODCALLTYPE Stop()override{return E_NOTIMPL;}HRESULT STDMETHODCALLTYPE Reset()override{return E_NOTIMPL;}
 HRESULT STDMETHODCALLTYPE SetEventHandle(HANDLE)override{return E_NOTIMPL;}HRESULT STDMETHODCALLTYPE GetService(REFIID,void**)override{return E_NOTIMPL;}
};
enum ControlMode {Success,GetFailure,ActivationFailure,Pending,PendingGetFailure};
class FakeOperation final:public IActivateAudioInterfaceAsyncOperation {
 LONG refs=1;ControlMode mode;ComPtr<IActivateAudioInterfaceCompletionHandler> callback;ComPtr<FakeClient> client;
public:
 HANDLE event;
 FakeOperation(ControlMode mode,IActivateAudioInterfaceCompletionHandler* handler):mode(mode),callback(handler){++operations;client.Attach(new FakeClient());event=static_cast<Completion*>(handler)->event.value;}
 ~FakeOperation(){--operations;}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** value)override{if(!value)return E_POINTER;*value=nullptr;if(iid!=__uuidof(IUnknown)&&iid!=__uuidof(IActivateAudioInterfaceAsyncOperation))return E_NOINTERFACE;*value=static_cast<IActivateAudioInterfaceAsyncOperation*>(this);AddRef();return S_OK;}
 ULONG STDMETHODCALLTYPE AddRef()override{return InterlockedIncrement(&refs);}ULONG STDMETHODCALLTYPE Release()override{ULONG n=InterlockedDecrement(&refs);if(!n)delete this;return n;}
 HRESULT STDMETHODCALLTYPE GetActivateResult(HRESULT* result,IUnknown** value)override{++resultCalls;*result=mode==ActivationFailure?E_ACCESSDENIED:S_OK;*value=client.Get();client->AddRef();return mode==GetFailure||mode==PendingGetFailure?E_FAIL:S_OK;}
 void Invoke(){Need(callback->ActivateCompleted(this)==S_OK,"callback-result");}
};
static ControlMode mode;static ComPtr<FakeOperation> held;
static std::vector<DWORD> activationWaits;
DWORD WINAPI ObservedWait(HANDLE handle,DWORD timeout){if(held&&handle==held->event)activationWaits.push_back(timeout);return WaitForSingleObject(handle,timeout);}
HRESULT WINAPI ControlledActivate(LPCWSTR path,REFIID iid,PROPVARIANT* parameters,IActivateAudioInterfaceCompletionHandler* callback,IActivateAudioInterfaceAsyncOperation** operation){
 Need(wcscmp(path,VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK)==0&&iid==__uuidof(IAudioClient),"real-process-loopback-activation-entry");
 Need(parameters&&parameters->vt==VT_BLOB,"real-activation-parameters");held.Attach(new FakeOperation(mode,callback));*operation=held.Get();held->AddRef();if(mode<Pending)held->Invoke();return S_OK;
}
static void ClosedEvent(HANDLE event){SetLastError(0);Need(WaitForSingleObject(event,0)==WAIT_FAILED&&GetLastError()==ERROR_INVALID_HANDLE,"completion-event-released");}
struct Pipe {Handle read,write;Pipe(){Need(CreatePipe(&read.value,&write.value,nullptr,4096)!=FALSE,"control-pipe");}void Unavailable(){unsigned char bytes[40]{};DWORD size=0;Need(ReadFile(read.value,bytes,40,&size,nullptr)&&size==40,"one-terminal-frame");Need(bytes[32]==4,"activation-unavailable-status");for(int i=24;i<32;i++)Need(bytes[i]==0,"zero-capture-time");for(int i=36;i<40;i++)Need(bytes[i]==0,"zero-feature-frame");}void Empty(){DWORD available=0;Need(PeekNamedPipe(read.value,nullptr,0,nullptr,&available,nullptr)&&available==0,"no-late-output-after-terminal");}};
static unsigned long long Created(){FILETIME born{},end{},kernel{},user{};Need(GetProcessTimes(GetCurrentProcess(),&born,&end,&kernel,&user)!=FALSE,"actual-source-creation");return (static_cast<unsigned long long>(born.dwHighDateTime)<<32)|born.dwLowDateTime;}
static void Run(ControlMode selected,const char* name){mode=selected;initializations=resultCalls=0;activationWaits.clear();Pipe pipe;Output output(pipe.write.value);auto began=GetTickCount64();int result=Capture(GetCurrentProcessId(),Created(),output);auto elapsed=GetTickCount64()-began;Need(activationWaits.size()==1&&activationWaits[0]==2000,"activation-wait-parameter-exactly-2000ms");Need(result==11,"activation-terminal-return");pipe.Unavailable();HANDLE event=held->event;
 if(selected>=Pending){Need(elapsed>=1900,"real-2000ms-activation-wait-not-skipped");Need(initializations==0&&resultCalls==0&&WaitForSingleObject(event,0)==WAIT_TIMEOUT,"timeout-keeps-pending-callback-owned");std::exception_ptr failure;std::thread callback([&]{try{held->Invoke();}catch(...){failure=std::current_exception();}});callback.join();if(failure)std::rethrow_exception(failure);Need(initializations==0&&resultCalls==1,"late-callback-must-not-initialize");Need(WaitForSingleObject(event,0)==WAIT_OBJECT_0,"late-completion-published-only-to-owned-event");}
 else if(selected==Success)Need(initializations==1&&resultCalls==1,"on-time-success-reaches-initialize");
 else Need(initializations==0&&resultCalls==1,"failed-activation-result-must-not-initialize");
 pipe.Empty();held.Reset();Need(clients==0&&operations==0,"activation-COM-references-released");ClosedEvent(event);
 std::printf("{\"case\":\"%s\",\"wait_ms\":%llu,\"wait_parameter_ms\":%lu,\"initializations\":%d,\"result_calls\":%d,\"objects_remaining\":0,\"event_closed\":true}\n",name,elapsed,activationWaits[0],initializations,resultCalls);
}
int main(){try{Run(Success,"on-time-success");Run(GetFailure,"GetActivateResult-failure");Run(ActivationFailure,"activation-HRESULT-failure");Run(Pending,"timeout-then-late-success");Run(PendingGetFailure,"timeout-then-late-GetActivateResult-failure");return 0;}catch(const std::exception& error){std::fprintf(stderr,"FAIL %s\n",error.what());held.Reset();std::fprintf(stderr,"CLEANUP activation-objects=%d\n",clients+operations);return clients==0&&operations==0?1:2;}}
