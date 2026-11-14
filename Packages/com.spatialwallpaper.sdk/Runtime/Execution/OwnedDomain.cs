using System;
using System.Collections.Generic;
using SpatialWallpaper.Sdk.Security;
using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Execution {
 public abstract class OwnedValue {
  internal readonly OwnedDomain Domain;internal bool Alive=true;internal ResourceLease Lease;internal LinkedListNode<OwnedDomain.Registration> Entry;
  protected OwnedValue(OwnedDomain domain){Domain=domain;}
  ~OwnedValue(){Domain.Reclaim(this);}
 }
 public sealed class OwnedHandle:OwnedValue {
  public string Kind{get;}internal readonly object Native;
  internal OwnedHandle(OwnedDomain domain,string kind,object native):base(domain){Kind=kind;Native=native;}
 }
 public sealed class OwnedArray:OwnedValue {
  internal object[] Items;internal readonly string TypeId;public string ElementType{get;}public int Count=>Items.Length;
  internal OwnedArray(OwnedDomain domain,string type,object[] items):base(domain){ElementType=type;TypeId=type+"[]";Items=items;}
 }
 internal sealed class OwnedBuffer {
  internal readonly object[] Items;readonly ResourceLease lease;
  internal OwnedBuffer(object[] items,ResourceLease reservation){Items=items;lease=reservation;}
  ~OwnedBuffer(){lease.Dispose();}
 }
 public sealed class OwnedList:OwnedValue {
  internal OwnedBuffer Buffer;internal object[] Items=>Buffer.Items;internal int Length;internal readonly string TypeId;
  public string ElementType{get;}public int Count=>Length;
  internal OwnedList(OwnedDomain domain,string type,OwnedBuffer buffer,int count):base(domain){ElementType=type;TypeId="list_"+type;Buffer=buffer;Length=count;}
 }
 public sealed class ScriptObject:OwnedValue {
  public string ClassId{get;}internal readonly Dictionary<string,object> Fields;
  internal ScriptObject(OwnedDomain domain,string type,int fields):base(domain){ClassId=type;Fields=new Dictionary<string,object>(fields,StringComparer.Ordinal);}
 }
 public readonly struct VectorValue {
  public readonly float X,Y,Z,W;public readonly string Type;
  public VectorValue(string type,float x,float y,float z,float w=0){Type=type;X=x;Y=y;Z=z;W=w;}
 }
 public sealed class OwnedDomain:IDisposable {
  internal readonly struct Registration {
   internal readonly long Sequence;internal readonly WeakReference<OwnedValue> Value;internal readonly ResourceLease Lease;
   internal Registration(long sequence,OwnedValue value,ResourceLease lease){Sequence=sequence;Value=new WeakReference<OwnedValue>(value);Lease=lease;}
  }
  static readonly int MaxArray=RuleCatalog.Current.Limit("resources"),MaxString=(int)RuleCatalog.Current.Constraint("string")["max_chars"],MaxIntList=(int)RuleCatalog.Current.Constraint("list_i32")["max_elements"],MaxFloatList=(int)RuleCatalog.Current.Constraint("list_f32")["max_elements"];
  static int ListLimit(string type){if(type=="i32")return MaxIntList;if(type=="f32")return MaxFloatList;throw new System.InvalidOperationException("list-type");}
  readonly object gate=new object();readonly LinkedList<Registration> values=new LinkedList<Registration>();HashSet<OwnedValue> seen;
  readonly Dictionary<AdmittedBuild,ResourceLease> snapshots=new Dictionary<AdmittedBuild,ResourceLease>();string buildId;
  readonly ResourceLease traversalMemory;readonly bool ownsLedger;long sequence;bool disposed;
  public Guid Instance{get;}public uint Generation{get;}public ResourceLedger Ledger{get;}
  // Shared measured reference policy; platform/public support still requires full SDK acceptance.
  public OwnedDomain(Guid instance,uint generation):this(instance,generation,new ResourceLedger(new ResourceCost(RuntimeLimits.ManagedBytes,RuntimeLimits.NativeBytes,RuntimeLimits.GpuEstimateBytes,RuntimeLimits.Objects)),true){}
  public OwnedDomain(Guid instance,uint generation,ResourceLedger ledger):this(instance,generation,ledger,false){}
  OwnedDomain(Guid instance,uint generation,ResourceLedger ledger,bool own){
   if(instance==Guid.Empty||generation==0||ledger==null)throw new ArgumentException("trusted-instance-generation-budget");
   Instance=instance;Generation=generation;Ledger=ledger;ownsLedger=own;
   if(ledger.Limit.Objects>65536)throw new ArgumentOutOfRangeException("owned-object-capacity");
   traversalMemory=ledger.Reserve(new ResourceCost(checked(1024+ledger.Limit.Objects*48),0,0,0));
   try{seen=new HashSet<OwnedValue>((int)ledger.Limit.Objects);traversalMemory.Commit();}catch{traversalMemory.Dispose();throw;}
  }
  internal void AttachBuild(AdmittedBuild build){
   lock(gate){
    Live();if(buildId!=null&&buildId!=build.BuildId)throw new InvalidOperationException("domain-build-mismatch");
    if(snapshots.ContainsKey(build))return;
    var lease=Ledger.Reserve(new ResourceCost(build.SnapshotManagedBytes,0,0,0));
    try{snapshots.Add(build,lease);lease.Commit();buildId=build.BuildId;}catch{lease.Dispose();throw;}
   }
  }
  void Live(){if(disposed)throw new InvalidOperationException("domain-stopped");}
  T Track<T>(T value,ResourceLease lease)where T:OwnedValue{
   try{lock(gate){Live();value.Lease=lease;value.Entry=values.AddLast(new Registration(checked(++sequence),value,lease));lease.Commit();return value;}}
   catch{lease.Dispose();throw;}
  }
  internal void Reclaim(OwnedValue value){
   lock(gate){if(value.Entry!=null){if(ReferenceEquals(value.Entry.List,values))values.Remove(value.Entry);value.Entry=null;}value.Alive=false;value.Lease?.Dispose();}
  }
  public OwnedHandle Register(string kind,object native){
   lock(gate){
    Live();if(native==null||!(kind=="node"||kind=="prefab"||kind=="material"||kind=="mesh"||kind=="texture"||kind=="cubemap"||kind=="light"||kind=="sound"))throw new ArgumentException("handle-kind");
    foreach(var registration in values)if(registration.Value.TryGetTarget(out var value)&&value is OwnedHandle h&&h.Alive&&h.Kind==kind&&ReferenceEquals(h.Native,native))return h;
    var lease=Ledger.Reserve(new ResourceCost(224,0,0,1));
    try{return Track(new OwnedHandle(this,kind,native),lease);}catch{lease.Dispose();throw;}
   }
  }
  internal ScriptObject Object(string type,int fieldCount=0){
   Live();var lease=Ledger.Reserve(new ResourceCost(checked(320L+fieldCount*192L),0,0,1));
   try{return Track(new ScriptObject(this,type,fieldCount),lease);}catch{lease.Dispose();throw;}
  }
  internal static object DefaultValue(string type){if(type=="i32")return 0;if(type=="f32")return 0f;if(type=="bool")return false;if(type=="vector3"||type=="color")return new VectorValue(type,0,0,0);return null;}
  internal OwnedArray CreateArray(string type,int length,Func<int,object> initialize){
   Live();if(length<0||length>MaxArray)throw new InvalidOperationException("array-limit");
   var lease=Ledger.Reserve(new ResourceCost(checked(1024L+length*64L),0,0,1));
   try{
    var items=new object[length];
    for(int i=0;i<length;i++){items[i]=initialize==null?DefaultValue(type):initialize(i);RequireType(items[i],type);}
    return Track(new OwnedArray(this,type,items),lease);
   }catch{lease.Dispose();throw;}
  }
  public OwnedArray Array(string type,object[] items){
   if(items==null)throw new InvalidOperationException("array-limit");
   return CreateArray(type,items.Length,i=>items[i]);
  }
  OwnedBuffer Buffer(int capacity,object[] source,int count){
   var lease=Ledger.Reserve(new ResourceCost(checked(128L+capacity*64L),0,0,0));
   try{var items=new object[capacity];if(count>0)System.Array.Copy(source,items,count);var buffer=new OwnedBuffer(items,lease);lease.Commit();return buffer;}catch{lease.Dispose();throw;}
  }
  internal OwnedList CreateList(string type,int length,Func<int,object> initialize){
   Live();if(length<0||length>ListLimit(type))throw new InvalidOperationException("list-limit");
   var lease=Ledger.Reserve(new ResourceCost(512,0,0,1));OwnedBuffer buffer=null;
   try{
    buffer=Buffer(length,null,0);
    for(int i=0;i<length;i++){buffer.Items[i]=initialize(i);RequireType(buffer.Items[i],type);}
    return Track(new OwnedList(this,type,buffer,length),lease);
   }catch{lease.Dispose();throw;}
  }
  public OwnedList List(string type,object[] initial,int capacity=0){
   Live();int max=ListLimit(type);
   if(initial==null||capacity<0||Math.Max(capacity,initial.Length)>max)throw new InvalidOperationException("list-limit");
   foreach(var item in initial)RequireType(item,type);
   var lease=Ledger.Reserve(new ResourceCost(512,0,0,1));
   try{return Track(new OwnedList(this,type,Buffer(Math.Max(capacity,initial.Length),initial,initial.Length),initial.Length),lease);}catch{lease.Dispose();throw;}
  }
  public object Get(OwnedList list,int index){CheckDirect(list);if(index<0||index>=list.Count)throw new InvalidOperationException("list-index");var value=list.Items[index];Check(value);return value;}
  public void Set(OwnedList list,int index,object value){CheckDirect(list);if(index<0||index>=list.Count)throw new InvalidOperationException("list-index");RequireType(value,list.ElementType);list.Items[index]=value;}
  public void Add(OwnedList list,object value){
   CheckDirect(list);RequireType(value,list.ElementType);int max=ListLimit(list.ElementType);
   if(list.Length>=max)throw new InvalidOperationException("list-limit");
   if(list.Length==list.Items.Length){int capacity=Math.Min(max,Math.Max(4,list.Length*2));list.Buffer=Buffer(capacity,list.Items,list.Length);}
   list.Items[list.Length++]=value;
  }
  public void Clear(OwnedList list){CheckDirect(list);System.Array.Clear(list.Items,0,list.Length);list.Length=0;}
  public object Get(OwnedArray array,int index){CheckDirect(array);var value=array.Items[index];Check(value);return value;}
  public void Set(OwnedArray array,int index,object value){CheckDirect(array);RequireType(value,array.ElementType);array.Items[index]=value;}
  public void Invalidate(OwnedHandle handle){Check(handle);handle.Alive=false;}
  internal void InvalidateValue(OwnedValue value){if(!ReferenceEquals(value.Domain,this))throw new InvalidOperationException("foreign-owner");value.Alive=false;}
  internal long Checkpoint(){lock(gate){Live();return sequence;}}
  internal void Rollback(long checkpoint){
   lock(gate){
    Live();if(checkpoint<0||checkpoint>sequence)throw new ArgumentOutOfRangeException();
    var node=values.First;while(node!=null){var next=node.Next;if(node.Value.Sequence>checkpoint){if(node.Value.Value.TryGetTarget(out var value)){value.Alive=false;value.Entry=null;}node.Value.Lease.Dispose();values.Remove(node);}node=next;}
   }
  }
  internal void InvalidateHandles(Func<OwnedHandle,bool> predicate){
   lock(gate){Live();foreach(var registration in values)if(registration.Value.TryGetTarget(out var value)&&value is OwnedHandle handle&&handle.Alive&&predicate(handle))handle.Alive=false;}
  }
  static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
  internal void CheckDirect(object value){lock(gate){Live();if(value is OwnedValue owned){if(!ReferenceEquals(owned.Domain,this)||!owned.Alive)throw new InvalidOperationException("foreign-or-stale-value");return;}Walk(value,0);}}
  public void Check(object value){lock(gate){Live();seen.Clear();try{Walk(value,0);}finally{seen.Clear();}}}
  void Walk(object value,int depth,bool direct=true){
   if(depth>32)throw new InvalidOperationException("value-depth");
   if(value==null||value is int||value is bool)return;
   if(value is float f){if(float.IsNaN(f)||float.IsInfinity(f))throw new InvalidOperationException("non-finite");return;}
   if(value is string s){if(s.Length>MaxString)throw new InvalidOperationException("string-limit");return;}
   if(value is VectorValue v){if(!Finite(v.X)||!Finite(v.Y)||!Finite(v.Z)||!Finite(v.W))throw new InvalidOperationException("non-finite");return;}
   if(!(value is OwnedValue owned)||!ReferenceEquals(owned.Domain,this))throw new InvalidOperationException("foreign-or-stale-value");
   if(!owned.Alive){if(direct)throw new InvalidOperationException("stale-value");return;}
   if(!seen.Add(owned))return;
   if(value is OwnedArray a)foreach(var item in a.Items)Walk(item,depth+1,false);
   if(value is OwnedList list)for(int i=0;i<list.Length;i++)Walk(list.Items[i],depth+1,false);
   if(value is ScriptObject obj)foreach(var field in obj.Fields.Values)Walk(field,depth+1,false);
  }
  public void RequireType(object value,string type){
   Check(value);
   if(value==null){if(type=="i32"||type=="f32"||type=="bool"||type=="vector3"||type=="color"||type=="void")throw new InvalidOperationException("null-value-type");return;}
   bool fits=type=="i32"&&value is int||type=="f32"&&value is float||type=="bool"&&value is bool||type=="string"&&value is string||
    value is OwnedHandle h&&h.Kind==type||value is OwnedArray a&&a.TypeId==type||
    value is OwnedList l&&l.TypeId==type||value is ScriptObject o&&o.ClassId==type||value is VectorValue v&&v.Type==type;
   if(!fits)throw new InvalidOperationException("runtime-type:"+type);
  }
  internal object Unwrap(OwnedHandle handle,string type){RequireType(handle,type);return handle.Native;}
  public void Dispose(){
   lock(gate){
    if(disposed)return;disposed=true;
    while(values.First!=null){var node=values.First;if(node.Value.Value.TryGetTarget(out var value)){value.Alive=false;value.Entry=null;}node.Value.Lease.Dispose();values.RemoveFirst();}
    seen.Clear();seen=null;foreach(var lease in snapshots.Values)lease.Dispose();snapshots.Clear();traversalMemory.Dispose();if(ownsLedger)Ledger.Dispose();
   }
  }
 }
}
