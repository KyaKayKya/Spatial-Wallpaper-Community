#define NOMINMAX
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
// Test-only hostile helper. Production never accepts a Worker-selected executable.
static uint64_t Qpc(){LARGE_INTEGER n,f;QueryPerformanceCounter(&n);QueryPerformanceFrequency(&f);return n.QuadPart/f.QuadPart*10000000ULL+n.QuadPart%f.QuadPart*10000000ULL/f.QuadPart;}
static void Put(unsigned char* b,int at,uint64_t n,int count){for(int i=0;i<count;i++)b[at+i]=(unsigned char)(n>>(i*8));}
int main(int argc,char** argv){
 if(argc!=3)return 2;int mode=atoi(argv[1]);unsigned char b[40]={};DWORD wrote;
 Put(b,0,0x46415753,4);Put(b,4,2,4);Put(b,8,1,8);Put(b,16,GetTickCount64(),8);Put(b,24,Qpc(),8);Put(b,32,1,4);b[36]=5;
 if(mode==1){Sleep(30000);return 0;} // silent startup
 if(mode==2){WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),b,19,&wrote,nullptr);Sleep(30000);return 0;}
 if(mode==3)b[36]=16;
 if(mode==4)Put(b,24,Qpc()+100000000ULL,8);
 if(mode==5)Put(b,24,Qpc()-10000000ULL,8);
 if(mode==6){Put(b,32,2,4);} // terminal carrying data
 if(mode==7)Put(b,0,0,4);
 WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),b,40,&wrote,nullptr);
 if(mode==12){for(int i=2;i<=4;i++){Sleep(220);Put(b,8,i,8);Put(b,16,GetTickCount64(),8);Put(b,24,Qpc(),8);WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),b,40,&wrote,nullptr);}}
 if(mode==8){Put(b,8,2,8);WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),b,40,&wrote,nullptr);} // rate violation
 if(mode==9){WriteFile(GetStdHandle(STD_OUTPUT_HANDLE),b,40,&wrote,nullptr);} // replay
 if(mode==10)return 0; // unexpected EOF after active
 Sleep(30000);return 0;
}
