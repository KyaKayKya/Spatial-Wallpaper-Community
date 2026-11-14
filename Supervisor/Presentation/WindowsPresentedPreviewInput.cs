using System;using System.Drawing;using System.Windows.Forms;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Editor.Native;using SpatialWallpaper.Supervision.Broker;
namespace SpatialWallpaper.Supervision.Presentation {
 // Same receiver and physical region as the verified presenter. UI-thread owner.
 // Reuses the existing native focus proof and authenticated input channel.
 public sealed class WindowsPresentedPreviewInput:IDisposable {
  readonly Control target;readonly WindowsFramePresenter presenter;readonly int thread;PreviewInputChannel channel;WindowsPreviewInput source;bool disposed;
  public WindowsPresentedPreviewInput(Control target,WindowsFramePresenter presenter,RuntimeSession session,SupervisorManagerConnection connection){
   if(target==null||presenter==null||session==null||connection==null)throw new ArgumentNullException();presenter.RequireInputBinding(target,session);this.target=target;this.presenter=presenter;thread=System.Threading.Thread.CurrentThread.ManagedThreadId;
   try{channel=new PreviewInputChannel(connection.OpenPreviewInputStream(session).GetAwaiter().GetResult());source=new WindowsPreviewInput(target.Handle,channel,Proof,()=>target.Focus());target.HandleDestroyed+=Destroyed;}
   catch{Dispose();throw;}
  }
  bool Proof(bool focus,out int left,out int top,out int width,out int height){left=top=width=height=0;if(disposed||!presenter.TryGetPresentedRegion(out Rectangle region))return false;left=region.Left;top=region.Top;width=region.Width;height=region.Height;return true;}
  void Destroyed(object sender,EventArgs args){Dispose();}
  public void Dispose(){if(disposed)return;if(System.Threading.Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("preview-input-owner-thread");disposed=true;target.HandleDestroyed-=Destroyed;try{source?.Dispose();}finally{channel?.Dispose();}}
 }
}
