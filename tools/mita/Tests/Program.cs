using System.Reflection;

// Reflection lets an absent implementation fail as a named behavior assertion,
// rather than preventing the test harness from compiling.
var policyType=Assembly.GetExecutingAssembly().GetType("SpatialWallpaper.Mita.MitaAttention");
Check(policyType!=null,"one attention arbiter must exist");
dynamic New()=>Activator.CreateInstance(policyType,new object[]{7});
string Step(dynamic p,float now,bool fresh=false,bool near=false,bool moved=false,bool busy=false,bool available=true)=>
 p.Step(now,available,busy,fresh,near,moved).ToString();

var p=New();p.Reset(0f);
Check(Step(p,1)=="Native","no attention without input");
Check(Step(p,2,near:false,moved:true)=="Native","far-away desktop mouse cannot attract attention");
Check(Step(p,3,near:true,moved:true)=="Mouse","nearby moving mouse attracts attention");
Check(Step(p,4,near:true)=="Mouse","mouse has a short hold after movement");
Check(Step(p,4.21f,near:true)=="Native","stationary mouse releases after 1.2 seconds");
p.Click(5f);
Check(Step(p,5.2f,near:true,moved:true)=="Click","click takes priority over moving mouse");
Check(Step(p,8.99f,near:true,moved:true)=="Click","click remains locked for four seconds");
Check(Step(p,9.01f,near:true,moved:true)=="Mouse","current mouse becomes eligible after four-second click lock");
p.Reset(0f);
Check(Step(p,6.5f,near:true,moved:true)=="Mouse","moving near character starts a fresh mouse hold");
Check(Step(p,6.6f,near:false)=="Native","leaving character releases mouse");
p.Click(7f);
Check(Step(p,7.1f,busy:true)=="Native","original performance owns all bones");
Check(Step(p,7.2f)=="Native","performance clears old click requests");
Check(Step(p,8,fresh:true)=="Native","new presence must stabilize");
Check(Step(p,8.39f,fresh:true)=="Native","presence cannot trigger before 0.4 seconds");
Check(Step(p,8.41f,fresh:true)=="User","stable presence gets a brief greeting");
Check(Step(p,8.5f,fresh:false)=="Native","lost tracking immediately cancels automatic attention");
Check(Step(p,9,fresh:true)=="Native","recovered tracking also stabilizes first");
Check(Step(p,9.41f,fresh:true)=="User","recovered stable presence gets attention");
Check(Step(p,10.3f,fresh:true)=="Native","automatic greeting ends within 0.8 seconds");
float next=p.NextGlanceAt;
Check(next>=19.81f&&next<=50.21f,"next automatic glance waits 10 to 40 seconds after previous glance");
Check(Step(p,next-.01f,fresh:true)=="Native","no early random glance");
Check(Step(p,next+.01f,fresh:true)=="User","scheduled glance starts when due");
p.Click(60f);
Check(Step(p,60.1f,fresh:false)=="Click","explicit click works without camera tracking");
Check(Step(p,60.1f,fresh:false)=="Click","frozen scaled clock cannot advance click lock");
Check(Step(p,64.01f,fresh:false)=="Native","expired click does not require tracking");
p.Click(70f);
Check(Step(p,70.1f,available:false)=="Native","opening blocks gaze");
Check(Step(p,70.2f)=="Native","opening clears requests too");
Console.WriteLine("PASS: attention priority, mouse release, stable presence, tracking loss, cadence and paused clock");

static void Check(bool condition,string message){if(!condition)throw new Exception("FAIL: "+message);}
