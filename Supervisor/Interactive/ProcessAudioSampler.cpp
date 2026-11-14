// Trusted native helper. Parent owns its process/job and stdout pipe.
// Arguments come from trusted source selection: PID and creation FILETIME.
#define NOMINMAX
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>
#include <cstdint>
#include <limits>
#include "AudioFeatures.h"
#include "AudioTiming.h"
#pragma comment(lib,"ole32.lib")
#pragma comment(lib,"mmdevapi.lib")
#pragma comment(lib,"uuid.lib")
using Microsoft::WRL::ComPtr;
namespace {
struct Handle {HANDLE value=nullptr;~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}};
class Completion final:public IActivateAudioInterfaceCompletionHandler,public IAgileObject {
 LONG references=1;
public:
 Handle event;HRESULT result=E_PENDING;ComPtr<IAudioClient> client;
 Completion(){event.value=CreateEventW(nullptr,TRUE,FALSE,nullptr);}
 HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** value) override {
  if(!value)return E_POINTER;*value=nullptr;
  if(iid==__uuidof(IUnknown)||iid==__uuidof(IActivateAudioInterfaceCompletionHandler))*value=static_cast<IActivateAudioInterfaceCompletionHandler*>(this);
  else if(iid==__uuidof(IAgileObject))*value=static_cast<IAgileObject*>(this);else return E_NOINTERFACE;
  AddRef();return S_OK;
 }
 ULONG STDMETHODCALLTYPE AddRef() override{return InterlockedIncrement(&references);}
 ULONG STDMETHODCALLTYPE Release() override{ULONG count=InterlockedDecrement(&references);if(!count)delete this;return count;}
 HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation) override {
  ComPtr<IUnknown> unknown;HRESULT activated=E_FAIL;
  result=operation->GetActivateResult(&activated,&unknown);
  if(SUCCEEDED(result))result=activated;
  if(SUCCEEDED(result))result=unknown?unknown.As(&client):E_NOINTERFACE;
  SetEvent(event.value);return S_OK;
 }
};
enum Status:std::uint32_t {Active=1,SourceEnded=2,SourceRejected=3,Unavailable=4,Fault=5};
class Output {
 HANDLE pipe;std::uint64_t sequence=0;
 static void Put(unsigned char* data,unsigned at,std::uint64_t value,unsigned bytes){for(unsigned i=0;i<bytes;i++)data[at+i]=static_cast<unsigned char>(value>>(8*i));}
public:
 explicit Output(HANDLE handle):pipe(handle){}
 bool Send(Status status,const SpatialAudio::StereoFeatureReducer* features=nullptr,std::uint64_t capturedQpc100ns=0){
  if(sequence==std::numeric_limits<std::uint64_t>::max())return false;
  unsigned char data[40]{};Put(data,0,0x46415753,4);Put(data,4,2,4);Put(data,8,++sequence,8);Put(data,16,GetTickCount64(),8);Put(data,24,capturedQpc100ns,8);Put(data,32,status,4);
  if(status==Active&&features&&capturedQpc100ns)for(unsigned i=0;i<4;i++)data[36+i]=features->Band(i);
  DWORD written=0;return WriteFile(pipe,data,sizeof(data),&written,nullptr)&&written==sizeof(data);
 }
};
bool Number(const char* text,std::uint64_t& value){
 value=0;if(!text||!*text)return false;
 for(;*text;text++){if(*text<'0'||*text>'9')return false;unsigned digit=*text-'0';if(value>(std::numeric_limits<std::uint64_t>::max()-digit)/10)return false;value=value*10+digit;}return true;
}
std::uint64_t Qpc100ns(){LARGE_INTEGER ticks{},frequency{};QueryPerformanceCounter(&ticks);QueryPerformanceFrequency(&frequency);return ticks.QuadPart/frequency.QuadPart*10000000ull+(ticks.QuadPart%frequency.QuadPart)*10000000ull/frequency.QuadPart;}
int Capture(DWORD pid,std::uint64_t creation,Output& output){
 Handle source;source.value=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
 FILETIME born{},ended{},kernel{},user{};
 if(!source.value||!GetProcessTimes(source.value,&born,&ended,&kernel,&user)||
    ((static_cast<std::uint64_t>(born.dwHighDateTime)<<32)|born.dwLowDateTime)!=creation||WaitForSingleObject(source.value,0)!=WAIT_TIMEOUT){output.Send(SourceRejected);return 10;}
 AUDIOCLIENT_ACTIVATION_PARAMS parameters{};parameters.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
 parameters.ProcessLoopbackParams.TargetProcessId=pid;parameters.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
 PROPVARIANT variant{};variant.vt=VT_BLOB;variant.blob.cbSize=sizeof(parameters);variant.blob.pBlobData=reinterpret_cast<BYTE*>(&parameters);
 ComPtr<Completion> completion;completion.Attach(new Completion());if(!completion->event.value){output.Send(Unavailable);return 11;}
 ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
 HRESULT hr=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&variant,completion.Get(),&operation);
 if(FAILED(hr)||WaitForSingleObject(completion->event.value,2000)!=WAIT_OBJECT_0){output.Send(Unavailable);return 11;}
 // The event is the only publication barrier for callback fields.
 hr=completion->result;ComPtr<IAudioClient> client=completion->client;
 if(FAILED(hr)||!client){output.Send(Unavailable);return 11;}
 WAVEFORMATEX format{};format.wFormatTag=WAVE_FORMAT_PCM;format.nChannels=2;format.nSamplesPerSec=48000;format.wBitsPerSample=16;format.nBlockAlign=4;format.nAvgBytesPerSec=192000;
 hr=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,1000000,0,&format,nullptr);
 ComPtr<IAudioCaptureClient> capture;if(SUCCEEDED(hr))hr=client->GetService(__uuidof(IAudioCaptureClient),reinterpret_cast<void**>(capture.GetAddressOf()));
 if(SUCCEEDED(hr))hr=client->Start();if(FAILED(hr)){output.Send(Unavailable);return 11;}
 struct Stop {IAudioClient* value;~Stop(){value->Stop();}} stop{client.Get()};
 SpatialAudio::StereoFeatureReducer reducer;SpatialAudio::EmissionGate emission;SpatialAudio::SampleFreshness freshness;
 auto clock=[](){return GetTickCount64();};
 if(!emission.TrySend(clock,[&](){return output.Send(Active);}))return 12;
 for(;;){
  if(WaitForSingleObject(source.value,0)!=WAIT_TIMEOUT){reducer.Reset();output.Send(SourceEnded);return 0;}
  UINT32 size=0;hr=capture->GetNextPacketSize(&size);if(FAILED(hr)){output.Send(Fault);return 13;}
  if(size){
   BYTE* data=nullptr;UINT32 frames=0;DWORD flags=0;UINT64 timestamp=0;
   hr=capture->GetBuffer(&data,&frames,&flags,nullptr,&timestamp);if(FAILED(hr)){output.Send(Fault);return 13;}
   std::uint64_t nowQpc=Qpc100ns();bool stale=(flags&AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR)||!freshness.Observe(timestamp,nowQpc);
   if(frames>48000){capture->ReleaseBuffer(frames);output.Send(Fault);return 13;}
   if(stale){reducer.Reset();freshness.Reset();}
   else {
    if(flags&AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY)reducer.Reset();
    auto values=reinterpret_cast<const short*>(data);
    for(UINT32 i=0;i<frames;i++){
     bool silent=(flags&AUDCLNT_BUFFERFLAGS_SILENT)!=0;
     reducer.Push(silent?0:values[i*2]/32768.0f,silent?0:values[i*2+1]/32768.0f);
    }
   }
   hr=capture->ReleaseBuffer(frames);if(FAILED(hr)){output.Send(Fault);return 13;}
  }
  if(!freshness.Fresh(Qpc100ns()))reducer.Reset();
   if(WaitForSingleObject(source.value,0)!=WAIT_TIMEOUT){reducer.Reset();output.Send(SourceEnded);return 0;}
   if(!emission.TrySend(clock,[&](){bool fresh=freshness.Fresh(Qpc100ns());if(!fresh)reducer.Reset();return output.Send(Active,&reducer,fresh?freshness.Captured():0);}))return 12;
  if(!size)Sleep(3);
 }
}
}
int main(int argc,char** argv){
 HANDLE pipe=GetStdHandle(STD_OUTPUT_HANDLE);if(GetFileType(pipe)!=FILE_TYPE_PIPE)return 1;Output output(pipe);
 std::uint64_t pid=0,creation=0;
 if(argc!=3||!Number(argv[1],pid)||!Number(argv[2],creation)||pid==0||pid>MAXDWORD||creation==0){output.Send(SourceRejected);return 10;}
 HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(hr)){output.Send(Unavailable);return 11;}
 int result=0;try{result=Capture(static_cast<DWORD>(pid),creation,output);}catch(...){output.Send(Fault);result=13;}
 CoUninitialize();return result;
}
