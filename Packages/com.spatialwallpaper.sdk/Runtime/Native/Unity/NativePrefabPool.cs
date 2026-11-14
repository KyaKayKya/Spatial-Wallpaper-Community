using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SpatialWallpaper.Sdk.Native.Unity {
 public sealed class NativePrefabPool:IDisposable {
  public sealed class Lease {
   internal long Prefab;internal bool Borrowed,Broken,Active,Published;
   public GameObject Root{get;internal set;}
   public Transform[] Nodes{get;internal set;}
  }
  readonly NativeAssetCatalog assets;readonly Transform staging;
  readonly Dictionary<long,Stack<Lease>> available=new Dictionary<long,Stack<Lease>>();
  readonly HashSet<Lease> owned=new HashSet<Lease>();
  sealed class Reset {internal Lease Lease;internal int Index;}
  readonly Queue<Reset> resetting=new Queue<Reset>();
  int totalNodes;bool disposed;
  public double LargestPreparationMilliseconds{get;private set;}
  public int BorrowedCount{get;private set;}
  public int PendingResets=>resetting.Count;
  public double LargestResetNodeMilliseconds{get;private set;}
  public NativePrefabPool(NativeAssetCatalog assets,Transform owner){
   this.assets=assets??throw new ArgumentNullException(nameof(assets));if(!owner)throw new ArgumentException("native-prefab-owner");var root=new GameObject("Native prepared prefabs");root.SetActive(false);root.transform.SetParent(owner,false);staging=root.transform;
  }
  void Live(){if(disposed)throw new ObjectDisposedException(nameof(NativePrefabPool));}
  static void Admit(GameObject prefab){
   foreach(var component in prefab.GetComponentsInChildren<Component>(true)){
    if(!component)throw new InvalidDataException("native-prefab-missing-component");var type=component.GetType();
    bool allowed=type==typeof(Transform)||type==typeof(MeshFilter)||type==typeof(MeshRenderer)||type==typeof(SkinnedMeshRenderer)||type==typeof(Light)||type==typeof(Animator)||type==typeof(BoxCollider)||type==typeof(SphereCollider)||type==typeof(CapsuleCollider)||type==typeof(MeshCollider)||type==typeof(Rigidbody)||type==typeof(LODGroup)||type==typeof(ParticleSystem)||type==typeof(ParticleSystemRenderer)||type==typeof(TrailRenderer)||type==typeof(LineRenderer);
    if(!allowed)throw new InvalidDataException("native-prefab-component-not-admitted:"+type.FullName);
    if(component is ParticleSystem particles&&(particles.main.stopAction==ParticleSystemStopAction.Destroy||particles.main.stopAction==ParticleSystemStopAction.Callback))throw new InvalidDataException("native-prefab-particle-destroy-or-callback");
    if(component is Animator animator){if(animator.GetBehaviours<StateMachineBehaviour>().Length!=0)throw new InvalidDataException("native-prefab-animator-behaviour");if(animator.runtimeAnimatorController)foreach(var clip in animator.runtimeAnimatorController.animationClips)if(clip.events.Length!=0)throw new InvalidDataException("native-prefab-animation-event");}
   }
  }
  public IEnumerator Prewarm(long prefab,int copies){
   Live();if(copies<1||copies>32)throw new ArgumentOutOfRangeException(nameof(copies));var source=assets.Require<GameObject>(prefab,"prefab");Admit(source);int count=source.GetComponentsInChildren<Transform>(true).Length;
   if(count==0||count>SceneCommandCatalog.MaxCommands||copies>(SceneCommandCatalog.MaxObjects-totalNodes)/count)throw new InvalidOperationException("native-prefab-pool-budget");
   if(!available.TryGetValue(prefab,out var ready)){ready=new Stack<Lease>();available.Add(prefab,ready);}
   for(int index=0;index<copies;index++){
    Live();if(count>SceneCommandCatalog.MaxObjects-totalNodes)throw new InvalidOperationException("native-prefab-pool-budget");var timer=System.Diagnostics.Stopwatch.StartNew();var clone=UnityEngine.Object.Instantiate(source,staging,false);clone.SetActive(false);var lease=new Lease{Prefab=prefab,Root=clone,Nodes=clone.GetComponentsInChildren<Transform>(true),Active=source.activeSelf};owned.Add(lease);ready.Push(lease);totalNodes+=count;timer.Stop();LargestPreparationMilliseconds=Math.Max(LargestPreparationMilliseconds,timer.Elapsed.TotalMilliseconds);yield return null;
   }
  }
  public Lease Borrow(long prefab){Live();if(!available.TryGetValue(prefab,out var ready)||ready.Count==0)throw new InvalidOperationException("native-prefab-not-prewarmed");var lease=ready.Pop();lease.Borrowed=true;BorrowedCount++;return lease;}
  public void Publish(Lease lease){Live();if(lease==null||!owned.Contains(lease)||!lease.Borrowed)throw new InvalidOperationException("native-prefab-lease-owner");lease.Root.transform.SetParent(staging.parent,false);lease.Published=true;lease.Root.SetActive(lease.Active);}
  public void InvalidateSubtree(Lease lease){Live();if(lease==null||!owned.Contains(lease)||!lease.Borrowed)throw new InvalidOperationException("native-prefab-lease-owner");lease.Broken=true;}
  public void Return(Lease lease){
   Live();if(lease==null||!owned.Contains(lease)||!lease.Borrowed)throw new InvalidOperationException("native-prefab-lease-owner");lease.Root.SetActive(false);lease.Borrowed=false;BorrowedCount--;
   if(lease.Broken){owned.Remove(lease);totalNodes-=lease.Nodes.Length;UnityEngine.Object.Destroy(lease.Root);return;}
   lease.Root.transform.SetParent(staging,false);if(!lease.Published){available[lease.Prefab].Push(lease);return;}lease.Published=false;resetting.Enqueue(new Reset{Lease=lease});
  }
  public void Step(double budgetMilliseconds=2){
   Live();if(double.IsNaN(budgetMilliseconds)||budgetMilliseconds<=0||budgetMilliseconds>2)throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));if(resetting.Count==0)return;var phase=System.Diagnostics.Stopwatch.StartNew();
   while(resetting.Count>0&&phase.Elapsed.TotalMilliseconds<budgetMilliseconds){
    var reset=resetting.Peek();var lease=reset.Lease;var template=assets.Definitions.Single(value=>value.Id==lease.Prefab).Template;if(template.Count!=lease.Nodes.Length)throw new InvalidOperationException("native-prefab-template-changed");int index=reset.Index;var node=lease.Nodes[index];if(!node)throw new InvalidOperationException("native-prefab-pooled-node-missing");var timer=System.Diagnostics.Stopwatch.StartNew();var state=template[index].State;var position=state["position"];var rotation=state["rotation"];var scale=state["scale"];node.localPosition=new Vector3((float)position[0],(float)position[1],(float)position[2]);node.localRotation=new Quaternion((float)rotation[0],(float)rotation[1],(float)rotation[2],(float)rotation[3]);node.localScale=new Vector3((float)scale[0],(float)scale[1],(float)scale[2]);if(index>0)node.gameObject.SetActive((bool)state["active"]);var light=node.GetComponent<Light>();if(light)light.intensity=(float)state["intensity"];var body=node.GetComponent<Rigidbody>();if(body){body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.Sleep();}var animator=node.GetComponent<Animator>();if(animator&&animator.runtimeAnimatorController)animator.Rebind();timer.Stop();LargestResetNodeMilliseconds=Math.Max(LargestResetNodeMilliseconds,timer.Elapsed.TotalMilliseconds);
    if(++reset.Index==template.Count){resetting.Dequeue();available[lease.Prefab].Push(lease);}
   }
  }
  public void Dispose(){if(disposed)return;disposed=true;foreach(var lease in owned)if(lease.Root){lease.Root.SetActive(false);UnityEngine.Object.Destroy(lease.Root);}owned.Clear();available.Clear();resetting.Clear();BorrowedCount=0;totalNodes=0;if(staging)UnityEngine.Object.Destroy(staging.gameObject);}
 }
}
