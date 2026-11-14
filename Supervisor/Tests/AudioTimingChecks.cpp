#include "../Interactive/AudioTiming.h"
#include <vector>
#include <cstdio>
int main(){
 SpatialAudio::EmissionGate output;std::uint64_t clock=0;std::vector<std::uint64_t> frames;
 output.TrySend([&](){return clock;},[&](){frames.push_back(clock);return true;});
 clock=200;
 output.TrySend([&](){return clock;},[&](){clock=350;frames.push_back(clock);return true;});
 clock=400;
 output.TrySend([&](){return clock;},[&](){frames.push_back(clock);return true;});
 bool rate=frames.size()==2;
 SpatialAudio::SampleFreshness fresh;bool accepted=fresh.Observe(1000000,4900000);
 bool stale=!fresh.Fresh(5100000);
 std::printf("delayed-send-rate=%s capture-age-expiry=%s\n",rate?"PASS":"FAIL",accepted&&stale?"PASS":"FAIL");
 return rate&&accepted&&stale?0:1;
}
