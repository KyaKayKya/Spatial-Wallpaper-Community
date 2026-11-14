#include "../Interactive/AudioFeatures.h"
#include <cstdio>
#include <stdexcept>
#include <cmath>
static void Need(bool value,const char* why){if(!value)throw std::runtime_error(why);}
int main(){try{
 const double frequencies[]={80,400,1600,7000};
 for(int band=0;band<4;band++){
  SpatialAudio::FeatureReducer reducer;unsigned emitted=0;
  for(int i=0;i<48000;i++){
   bool ready=reducer.Push(static_cast<float>(0.1*std::sin(6.283185307179586*frequencies[band]*i/48000)));
   if(ready)emitted++;
  }
  Need(emitted==5,"feature-rate-not-five-Hz");
  for(int i=0;i<4;i++)Need(reducer.Band(i)<=15,"feature-exceeds-four-bits");
  for(int i=0;i<4;i++)if(i!=band)Need(reducer.Band(band)>reducer.Band(i),"tone-not-in-expected-band");
  reducer.Reset();for(int i=0;i<4;i++)Need(reducer.Band(i)==0,"reset-leaks-old-source");
  for(int i=0;i<9600;i++)reducer.Push(0);for(int i=0;i<4;i++)Need(reducer.Band(i)==0,"silence-nonzero");
 }
 SpatialAudio::FeatureReducer invalid;bool rejected=false;try{invalid.Push(NAN);}catch(const std::invalid_argument&){rejected=true;}Need(rejected,"NaN-accepted");
 rejected=false;try{invalid.Band(4);}catch(const std::out_of_range&){rejected=true;}Need(rejected,"band-outside-catalog");
 Need(sizeof(SpatialAudio::FeatureReducer)<512,"unbounded-audio-history");
 SpatialAudio::StereoFeatureReducer stereo;
 for(int i=0;i<48000;i++){float v=static_cast<float>(0.1*std::sin(6.283185307179586*80*i/48000));stereo.Push(0,v);}
 Need(stereo.Band(0)>0,"right-only-audio-lost");auto rightLevel=stereo.Band(0);stereo.Reset();
 for(int i=0;i<48000;i++){float v=static_cast<float>(0.1*std::sin(6.283185307179586*80*i/48000));stereo.Push(v,-v);}
 Need(stereo.Band(0)==rightLevel,"antiphase-stereo-cancelled");
 std::puts("PASS four bands, five Hz, four-bit values, silence/reset, invalid samples and bounded state");return 0;
}catch(const std::exception& e){std::fprintf(stderr,"%s\n",e.what());return 1;}}
