// Native mechanism acceptance only; synthetic sources, aggregate statistics only.
#define main activationProbeMain
#include "process_audio_support.cpp"
#undef main
#include <cmath>
#include <cstdlib>
#include <cstring>
#include "../../../Supervisor/Interactive/AudioFeatures.h"

static WAVEFORMATEX Format(bool floating){
 WAVEFORMATEX value{};value.wFormatTag=floating?WAVE_FORMAT_IEEE_FLOAT:WAVE_FORMAT_PCM;
 value.nChannels=2;value.nSamplesPerSec=48000;value.wBitsPerSample=floating?32:16;
 value.nBlockAlign=value.nChannels*value.wBitsPerSample/8;value.nAvgBytesPerSec=value.nSamplesPerSec*value.nBlockAlign;return value;
}
static int Tone(double frequency,bool rightOnly=false,DWORD duration=6000){
 IMMDeviceEnumerator* enumerator=nullptr;IMMDevice* device=nullptr;IAudioClient* client=nullptr;IAudioRenderClient* render=nullptr;
 HRESULT hr=CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,__uuidof(IMMDeviceEnumerator),reinterpret_cast<void**>(&enumerator));
 if(SUCCEEDED(hr))hr=enumerator->GetDefaultAudioEndpoint(eRender,eConsole,&device);
 if(SUCCEEDED(hr))hr=device->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,reinterpret_cast<void**>(&client));
 auto format=Format(true);
 if(SUCCEEDED(hr))hr=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,1000000,0,&format,nullptr);
 UINT32 capacity=0;if(SUCCEEDED(hr))hr=client->GetBufferSize(&capacity);
 if(SUCCEEDED(hr)&&capacity>48000)hr=E_INVALIDARG;
 if(SUCCEEDED(hr))hr=client->GetService(__uuidof(IAudioRenderClient),reinterpret_cast<void**>(&render));
 if(SUCCEEDED(hr))hr=client->Start();
 std::printf("{\"ready\":%s,\"hresult\":\"0x%08lx\"}\n",SUCCEEDED(hr)?"true":"false",static_cast<unsigned long>(hr));std::fflush(stdout);
 ULONGLONG start=GetTickCount64();unsigned long long sample=0;
 while(SUCCEEDED(hr)&&GetTickCount64()-start<duration){
  UINT32 padding=0;hr=client->GetCurrentPadding(&padding);if(FAILED(hr)||padding>capacity)break;
  UINT32 frames=capacity-padding;if(!frames){Sleep(3);continue;}
  BYTE* bytes=nullptr;hr=render->GetBuffer(frames,&bytes);if(FAILED(hr))break;
  float* values=reinterpret_cast<float*>(bytes);
  for(UINT32 i=0;i<frames;i++,sample++){float value=static_cast<float>(0.005*std::sin(6.283185307179586*frequency*sample/48000));values[i*2]=rightOnly?0:value;values[i*2+1]=value;}
  hr=render->ReleaseBuffer(frames,0);Sleep(3);
 }
 if(client)client->Stop();if(render)render->Release();if(client)client->Release();if(device)device->Release();if(enumerator)enumerator->Release();return FAILED(hr)?5:0;
}
struct Aggregate{unsigned long long count=0;double a=0,b=0,c=0,d=0;};
struct FeaturePacket{unsigned at=0;unsigned char bands[4]{};};
static int Capture(DWORD pid){
 // The harness owns this exact short-lived synthetic source PID.
 HANDLE target=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(!target)return 6;
 AUDIOCLIENT_ACTIVATION_PARAMS parameters{};parameters.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
 parameters.ProcessLoopbackParams.TargetProcessId=pid;parameters.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
 PROPVARIANT variant{};variant.vt=VT_BLOB;variant.blob.cbSize=sizeof(parameters);variant.blob.pBlobData=reinterpret_cast<BYTE*>(&parameters);
 auto completion=new Completion();IActivateAudioInterfaceAsyncOperation* operation=nullptr;
 HRESULT hr=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&variant,completion,&operation);
 if(SUCCEEDED(hr)&&WaitForSingleObject(completion->done,5000)!=WAIT_OBJECT_0)hr=HRESULT_FROM_WIN32(WAIT_TIMEOUT);
 if(SUCCEEDED(hr))hr=completion->query;if(SUCCEEDED(hr))hr=completion->result;
 IAudioClient* client=SUCCEEDED(hr)?completion->client:nullptr;
 if(SUCCEEDED(hr)&&!client)hr=E_NOINTERFACE;
 auto format=Format(false);IAudioCaptureClient* capture=nullptr;
 if(SUCCEEDED(hr))hr=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,1000000,0,&format,nullptr);
 if(SUCCEEDED(hr))hr=client->GetService(__uuidof(IAudioCaptureClient),reinterpret_cast<void**>(&capture));
 if(SUCCEEDED(hr))hr=client->Start();
 std::printf("{\"captureReady\":%s,\"hresult\":\"0x%08lx\"}\n",SUCCEEDED(hr)?"true":"false",static_cast<unsigned long>(hr));std::fflush(stdout);
 Aggregate slices[12];ULONGLONG start=GetTickCount64();unsigned long long sample=0;bool exited=false;
 SpatialAudio::FeatureReducer reducer;FeaturePacket features[16];unsigned featureCount=0;
 while(SUCCEEDED(hr)&&GetTickCount64()-start<3000){
  if(!exited&&WaitForSingleObject(target,0)==WAIT_OBJECT_0){exited=true;reducer.Reset();}
  UINT32 size=0;hr=capture->GetNextPacketSize(&size);if(FAILED(hr))break;if(!size){Sleep(3);continue;}
  BYTE* data=nullptr;UINT32 frames=0;DWORD flags=0;hr=capture->GetBuffer(&data,&frames,&flags,nullptr,nullptr);if(FAILED(hr))break;
  unsigned slot=static_cast<unsigned>((GetTickCount64()-start)/250);if(slot>11)slot=11;
  if(frames>48000||sample+frames>480000){capture->ReleaseBuffer(frames);hr=E_INVALIDARG;break;}
  for(UINT32 i=0;i<frames;i++,sample++){
   double value=(flags&AUDCLNT_BUFFERFLAGS_SILENT)?0:reinterpret_cast<short*>(data)[i*2]/32768.0;
   double angle=6.283185307179586*440*sample/48000;auto& slice=slices[slot];
   slice.count++;slice.a+=value*std::cos(angle);slice.b+=value*std::sin(angle);slice.c+=value*std::cos(angle*2);slice.d+=value*std::sin(angle*2);
   if(reducer.Push(exited?0:static_cast<float>(value))&&featureCount<16){auto& packet=features[featureCount++];packet.at=static_cast<unsigned>(GetTickCount64()-start);for(int band=0;band<4;band++)packet.bands[band]=reducer.Band(band);}
  }
  hr=capture->ReleaseBuffer(frames);
 }
 if(client)client->Stop();if(capture)capture->Release();
 std::printf("{\"targetExited\":%s,\"hresult\":\"0x%08lx\",\"slices\":[",exited?"true":"false",static_cast<unsigned long>(hr));
 for(int i=0;i<12;i++){auto& s=slices[i];std::printf("%s{\"samples\":%llu,\"target440\":%.8f,\"other880\":%.8f}",i?",":"",s.count,s.count?2*std::sqrt(s.a*s.a+s.b*s.b)/s.count:0,s.count?2*std::sqrt(s.c*s.c+s.d*s.d)/s.count:0);}
 std::printf("],\"features\":[");
 for(unsigned i=0;i<featureCount;i++){auto& p=features[i];std::printf("%s{\"atMs\":%u,\"bands\":[%u,%u,%u,%u]}",i?",":"",p.at,p.bands[0],p.bands[1],p.bands[2],p.bands[3]);}
 std::printf("]}\n");if(operation)operation->Release();completion->Release();CloseHandle(target);return FAILED(hr)?7:0;
}
int main(int argc,char** argv){
 if(argc!=3)return 1;HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(hr))return 2;
 int result=std::strcmp(argv[1],"--tone")==0?Tone(std::atof(argv[2])):
  std::strcmp(argv[1],"--tone-right")==0?Tone(std::atof(argv[2]),true):
  std::strcmp(argv[1],"--tone-right-long")==0?Tone(std::atof(argv[2]),true,90000):
  std::strcmp(argv[1],"--capture")==0?Capture(static_cast<DWORD>(std::strtoul(argv[2],nullptr,10))):1;
 CoUninitialize();return result;
}
