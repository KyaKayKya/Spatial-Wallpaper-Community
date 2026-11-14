// Probe process-loopback activation for this process only. No capture stream,
// microphone, system mix, waveform, or device enumeration is opened.
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <cstdio>
#include <cstring>
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"mmdevapi.lib")
#pragma comment(lib,"uuid.lib")

class Completion final:public IActivateAudioInterfaceCompletionHandler,public IAgileObject {
 LONG refs=1;
 DWORD delay;
public:
 explicit Completion(DWORD delayMilliseconds=0):delay(delayMilliseconds){}
 HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
 HRESULT result=E_PENDING,query=E_PENDING;
 IAudioClient* client=nullptr;
 ~Completion(){if(client)client->Release();if(done)CloseHandle(done);}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** output) override {
  if(!output)return E_POINTER;*output=nullptr;
  if(iid==__uuidof(IUnknown)||iid==__uuidof(IActivateAudioInterfaceCompletionHandler))
   *output=static_cast<IActivateAudioInterfaceCompletionHandler*>(this);
  else if(iid==__uuidof(IAgileObject))*output=static_cast<IAgileObject*>(this);
  else return E_NOINTERFACE;
  AddRef();return S_OK;
 }
 ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&refs);}
 ULONG STDMETHODCALLTYPE Release() override{ULONG value=InterlockedDecrement(&refs);if(!value)delete this;return value;}
 HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation) override {
  if(delay)Sleep(delay);
  IUnknown* value=nullptr;query=operation->GetActivateResult(&result,&value);
  if(value){if(SUCCEEDED(result))query=value->QueryInterface(__uuidof(IAudioClient),reinterpret_cast<void**>(&client));value->Release();}SetEvent(done);return S_OK;
 }
};
int main(int argc,char** argv){
 bool timeoutTest=argc==2&&std::strcmp(argv[1],"--timeout")==0;
 HRESULT initialized=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(initialized))return 2;
 AUDIOCLIENT_ACTIVATION_PARAMS parameters{};
 parameters.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
 parameters.ProcessLoopbackParams.TargetProcessId=GetCurrentProcessId();
 parameters.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
 PROPVARIANT variant{};variant.vt=VT_BLOB;variant.blob.cbSize=sizeof(parameters);variant.blob.pBlobData=reinterpret_cast<BYTE*>(&parameters);
 auto completion=new Completion(timeoutTest?250:0);if(!completion->done){completion->Release();CoUninitialize();return 3;}
 IActivateAudioInterfaceAsyncOperation* operation=nullptr;ULONGLONG start=GetTickCount64();
 HRESULT activated=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&variant,completion,&operation);
 DWORD waited=FAILED(activated)?WAIT_OBJECT_0:WaitForSingleObject(completion->done,timeoutTest?1:5000);
 HRESULT query=E_PENDING,result=E_PENDING;
 if(SUCCEEDED(activated)&&waited==WAIT_OBJECT_0){query=completion->query;result=completion->result;}
 std::printf("{\"activateHresult\":\"0x%08lx\",\"queryHresult\":\"0x%08lx\",\"resultHresult\":\"0x%08lx\",\"timedOut\":%s,\"elapsedMs\":%llu,\"target\":\"self-only\",\"captureStarted\":false}\n",
  static_cast<unsigned long>(activated),static_cast<unsigned long>(query),static_cast<unsigned long>(result),
  waited==WAIT_OBJECT_0?"false":"true",GetTickCount64()-start);
 if(operation)operation->Release();completion->Release();CoUninitialize();return waited==WAIT_OBJECT_0?0:4;
}
