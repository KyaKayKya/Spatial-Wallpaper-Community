#pragma once
#include <cmath>
#include <stdexcept>
#include <cstdint>

namespace SpatialAudio {
// Trusted-side reduction only. Fixed 48 kHz mono input; never exported to authors.
// Four broad bands, 200 ms non-overlapping energy windows, EMA coefficient 0.5,
// 4-bit logarithmic levels from -60 dBFS to 0 dBFS. No waveform/history buffer.
class FeatureReducer {
 struct LowPass {
  double b0,b1,b2,a1,a2,z1=0,z2=0;
  explicit LowPass(double hz){
   double k=std::tan(3.141592653589793*hz/48000),normal=1/(1+std::sqrt(2.0)*k+k*k);
   b0=k*k*normal;b1=2*b0;b2=b0;a1=2*(k*k-1)*normal;a2=(1-std::sqrt(2.0)*k+k*k)*normal;
  }
  double Push(double value){double result=b0*value+z1;z1=b1*value-a1*result+z2;z2=b2*value-a2*result;return result;}
  void Reset(){z1=z2=0;}
 } filters[4]={LowPass(200),LowPass(800),LowPass(3000),LowPass(10000)};
 double energy[4]{},smoothed[4]{};
 std::uint8_t values[4]{};unsigned count=0;
public:
 void Reset(){count=0;for(int i=0;i<4;i++){filters[i].Reset();energy[i]=smoothed[i]=0;values[i]=0;}}
 std::uint8_t Band(unsigned index)const {if(index>=4)throw std::out_of_range("audio-band");return values[index];}
 bool Push(float sample){
  if(!std::isfinite(sample)||sample < -1||sample > 1){Reset();throw std::invalid_argument("audio-sample");}
  double previous=0;
  for(int i=0;i<4;i++){double low=filters[i].Push(sample),band=low-previous;previous=low;energy[i]+=band*band;}
  if(++count<9600)return false;
  for(int i=0;i<4;i++){
   smoothed[i]=0.5*smoothed[i]+0.5*energy[i]/9600;energy[i]=0;
   double db=smoothed[i]>0?10*std::log10(smoothed[i]):-120;
   double level=std::floor((db+60)/4+0.5);values[i]=static_cast<std::uint8_t>(level<=0?0:level>=15?15:level);
  }
  count=0;return true;
 }
};
// Reduce channels independently so opposite phases cannot erase audible energy.
class StereoFeatureReducer {
 FeatureReducer left,right;
public:
 void Reset(){left.Reset();right.Reset();}
 bool Push(float a,float b){
  try{bool first=left.Push(a),second=right.Push(b);if(first!=second)throw std::logic_error("audio-channel-clock");return first;}
  catch(...){Reset();throw;}
 }
 std::uint8_t Band(unsigned index)const {auto a=left.Band(index),b=right.Band(index);return a>b?a:b;}
};
}
