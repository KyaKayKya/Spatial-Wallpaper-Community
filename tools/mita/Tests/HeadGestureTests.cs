using System.Reflection;
var assembly=Assembly.GetExecutingAssembly();
var type=assembly.GetType("SpatialWallpaper.Mita.MitaHeadGestureDetector");
Check(type!=null,"head gesture detector must exist");
dynamic Detector()=>Activator.CreateInstance(type);
string Sample(dynamic d,float t,float p,float y,bool valid=true)=>d.Observe(t,p,y,valid).ToString();
void Rest(dynamic d,float t=0){for(int i=0;i<=20;i++)Sample(d,t+i*.02f,0,0);}
var d=Detector();Rest(d);
Check(Sample(d,.5f,12,0)=="None","holding down is not a nod");
Check(Sample(d,.7f,12,0)=="None","holding a pose must not repeat");
Check(Sample(d,.9f,0,0)=="Nod","complete down and return is one nod");
Check(Sample(d,1f,0,0)=="None","neutral cannot repeat nod");
d=Detector();Rest(d);
Sample(d,.5f,0,15);Sample(d,.7f,0,0);Sample(d,.9f,0,-15);
Check(Sample(d,1.1f,0,0)=="Shake","left-right-return is one shake");
d=Detector();Rest(d);
for(int i=0;i<100;i++)Check(Sample(d,.5f+i*.02f,2*MathF.Sin(i),2*MathF.Cos(i))=="None","jitter must not trigger");
d=Detector();Rest(d);Sample(d,.5f,13,12);Check(Sample(d,.9f,0,0)=="None","ambiguous diagonal is not a deliberate gesture");
d=Detector();Rest(d);Sample(d,.5f,12,0,false);Check(Sample(d,.9f,0,0)=="None","lost sample cannot complete stale motion");
d=Detector();Rest(d);Sample(d,.5f,12,0);Sample(d,2f,12,0);Check(Sample(d,2.2f,0,0)=="None","slow held look must not become nod on return");
var queueType=assembly.GetType("SpatialWallpaper.Mita.MitaResponseQueue");Check(queueType!=null,"deterministic response queue must exist");
dynamic NewQueue()=>Activator.CreateInstance(queueType);
var kind=assembly.GetType("SpatialWallpaper.Mita.MitaHeadGesture");dynamic nod=Enum.Parse(kind,"Nod"),shake=Enum.Parse(kind,"Shake");
for(int count=1;count<=3;count++){
 dynamic q=NewQueue();for(int i=0;i<count;i++)Check(q.Accept(i*.2f,nod),"every detected nod below cap must be accepted");
 for(int i=0;i<count;i++){Check(q.StartNext(1f+i).ToString()=="Nod","accepted count must match actual starts");q.Complete(1.5f+i);}
 Check(q.StartNext(5f).ToString()=="None","no invented extra responses");
}
dynamic bounded=NewQueue();Check(bounded.Accept(0f,nod)&&bounded.Accept(.1f,shake)&&bounded.Accept(.2f,nod),"mixed gestures retain order");
Check(!bounded.Accept(.3f,shake),"fourth continuous gesture must be rejected");
Check(bounded.StartNext(.4f).ToString()=="Nod","first response order");bounded.Complete(.8f);
Check(bounded.StartNext(.9f).ToString()=="Shake","second response order");bounded.Complete(1.3f);
Check(bounded.StartNext(1.4f).ToString()=="Nod","third response order");bounded.Complete(1.8f);
Check(!bounded.Accept(2f,nod),"cooldown cannot accumulate stale gestures");
Check(bounded.Accept(4.81f,nod),"fresh gesture after three-second cooldown is accepted");
bounded.Reset();Check(bounded.StartNext(5f).ToString()=="None","tracking loss or performance reset discards queued requests");
Console.WriteLine("PASS complete nod/shake, hold/jitter/diagonal/loss/slow look; deterministic 1/2/3 responses, order, cap, cooldown and clearing");
static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);}
