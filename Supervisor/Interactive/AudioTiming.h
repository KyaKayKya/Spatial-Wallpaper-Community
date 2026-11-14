#pragma once
#include <cstdint>
namespace SpatialAudio {
class EmissionGate {
 bool sent=false;std::uint64_t last=0;
public:
 template<class Clock,class Write> bool TrySend(Clock clock,Write write){
  auto now=clock();if(sent&&(now<last||now-last<200))return true;
  if(!write())return false;last=clock();sent=true;return true;
 }
};
class SampleFreshness {
 bool seen=false;std::uint64_t captured=0;
public:
 void Reset(){seen=false;captured=0;}
 bool Observe(std::uint64_t sample,std::uint64_t now){
  if(sample>now||now-sample>4000000){Reset();return false;}
  captured=sample;seen=true;return true;
 }
 bool Fresh(std::uint64_t now)const{return seen&&now>=captured&&now-captured<=4000000;}
 std::uint64_t Captured()const{return seen?captured:0;}
};
}
