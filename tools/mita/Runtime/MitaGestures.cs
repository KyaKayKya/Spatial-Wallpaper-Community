using UnityEngine;

namespace SpatialWallpaper.Mita {
 public sealed class MitaGestures:MonoBehaviour {
  public Animator actor;public string[] names;public float[] durations;
  public bool Busy=>actor.GetCurrentAnimatorStateInfo(0).IsTag("Gesture")||actor.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Mita EndingFace")||actor.IsInTransition(0);
  public void Play(int index){if(Time.timeScale==0||GetComponent<MitaShot>().Clock<6.5f||Busy||index<0||index>=names.Length)return;GetComponent<MitaInteractions>().CancelHead();GetComponent<MitaLook>().ResetGaze();actor.SetLayerWeight(1,0);actor.ResetTrigger("Blink");actor.ResetTrigger("Pet");actor.Play("Face.None",1,0);actor.CrossFadeInFixedTime("Base Layer."+names[index],.3f);}
  void Update(){
   bool busy=Busy;actor.SetLayerWeight(1,busy||GetComponent<MitaShot>().Clock<6.5f?0:1);
   if(!GetComponent<MitaBehaviour>().InputEnabled)return;
   for(int i=0;i<names.Length;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))Play(i);
  }
 }
}
