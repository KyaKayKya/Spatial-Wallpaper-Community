// Target-process loopback statistics only. Never records or saves raw audio.
#define main soundActivationProbeMain
#include "../sdk/experiments/process_audio_support.cpp"
#undef main
#include <cmath>
#include <cstdlib>
int main(int argc,char** argv){
 if(argc!=3)return 1;DWORD pid=std::strtoul(argv[1],nullptr,10),duration=std::strtoul(argv[2],nullptr,10);if(!pid||duration<250||duration>15000)return 2;
 HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(hr))return 3;
 HANDLE target=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(!target){CoUninitialize();return 4;}
 AUDIOCLIENT_ACTIVATION_PARAMS parameters{};parameters.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;parameters.ProcessLoopbackParams.TargetProcessId=pid;parameters.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
 PROPVARIANT variant{};variant.vt=VT_BLOB;variant.blob.cbSize=sizeof(parameters);variant.blob.pBlobData=reinterpret_cast<BYTE*>(&parameters);
 auto completion=new Completion();IActivateAudioInterfaceAsyncOperation* operation=nullptr;
 hr=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&variant,completion,&operation);
 if(SUCCEEDED(hr)&&WaitForSingleObject(completion->done,5000)!=WAIT_OBJECT_0)hr=HRESULT_FROM_WIN32(WAIT_TIMEOUT);
 if(SUCCEEDED(hr))hr=completion->query;if(SUCCEEDED(hr))hr=completion->result;
 IAudioClient* client=SUCCEEDED(hr)?completion->client:nullptr;IAudioCaptureClient* capture=nullptr;
 WAVEFORMATEX format{};format.wFormatTag=WAVE_FORMAT_IEEE_FLOAT;format.nChannels=2;format.nSamplesPerSec=48000;format.wBitsPerSample=32;format.nBlockAlign=8;format.nAvgBytesPerSec=384000;
 if(SUCCEEDED(hr)&&!client)hr=E_NOINTERFACE;
 if(SUCCEEDED(hr))hr=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,1000000,0,&format,nullptr);
 if(SUCCEEDED(hr))hr=client->GetService(__uuidof(IAudioCaptureClient),reinterpret_cast<void**>(&capture));if(SUCCEEDED(hr))hr=client->Start();
 struct Bin {unsigned long long samples=0;double power=0,peak=0;};Bin bins[60];bool exited=false;auto start=GetTickCount64();
 while(SUCCEEDED(hr)&&GetTickCount64()-start<duration){
  if(WaitForSingleObject(target,0)==WAIT_OBJECT_0)exited=true;
  UINT32 size=0;hr=capture->GetNextPacketSize(&size);if(FAILED(hr))break;if(!size){Sleep(2);continue;}
  BYTE* data=nullptr;UINT32 frames=0;DWORD flags=0;hr=capture->GetBuffer(&data,&frames,&flags,nullptr,nullptr);if(FAILED(hr))break;
  if(frames>48000){capture->ReleaseBuffer(frames);hr=E_INVALIDARG;break;}
  unsigned slot=static_cast<unsigned>((GetTickCount64()-start)/250);if(slot>59)slot=59;
  for(UINT32 i=0;i<frames*2;i++){double value=(flags&AUDCLNT_BUFFERFLAGS_SILENT)?0:reinterpret_cast<float*>(data)[i];bins[slot].samples++;bins[slot].power+=value*value;bins[slot].peak=fmax(bins[slot].peak,fabs(value));}
  hr=capture->ReleaseBuffer(frames);
 }
 if(client)client->Stop();if(capture)capture->Release();
 std::printf("{\"pid\":%lu,\"target_exited\":%s,\"hresult\":\"0x%08lx\",\"bins\":[",pid,exited?"true":"false",static_cast<unsigned long>(hr));
 unsigned count=(duration+249)/250;for(unsigned i=0;i<count;i++)std::printf("%s{\"samples\":%llu,\"rms\":%.9f,\"peak\":%.9f}",i?",":"",bins[i].samples,bins[i].samples?sqrt(bins[i].power/bins[i].samples):0,bins[i].peak);std::printf("]}\n");
 if(operation)operation->Release();completion->Release();CloseHandle(target);CoUninitialize();return FAILED(hr)?5:0;
}
