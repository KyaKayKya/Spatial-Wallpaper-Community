using UnityEngine;

// Content asks for a cadence without knowing how eye samples arrive.
public sealed class ContinuousRenderDemand:MonoBehaviour {
 [Range(1,12)] public int interval=1;
 AdaptiveRenderCadence cadence;
 void OnEnable(){cadence=FindObjectOfType<AdaptiveRenderCadence>();if(cadence)cadence.RequestInterval(this,interval);}
 void OnDisable(){if(cadence)cadence.RequestInterval(this,0);cadence=null;}
 public void SetInterval(int value){interval=Mathf.Clamp(value,1,12);if(cadence&&isActiveAndEnabled)cadence.RequestInterval(this,interval);}
}
