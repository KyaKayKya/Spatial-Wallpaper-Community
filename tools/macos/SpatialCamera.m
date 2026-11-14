#import <AVFoundation/AVFoundation.h>
#import <Foundation/Foundation.h>
#import <CommonCrypto/CommonDigest.h>
#import <mach/mach_time.h>
#import <stdatomic.h>

typedef void (*Ready)(uint64_t,const char *);
typedef void (*Frame)(uint64_t,uint64_t,int,int,int,const void *);
typedef void (*Failure)(uint64_t,const char *);
typedef void (*Changed)(void);
static CFRunLoopRef cameraLoop;
static dispatch_queue_t frameQueue;
static AVCaptureSession *session;
static id delegate;
static id errorObserver;
static NSMutableArray *watchObservers;
static Changed changedCallback;
static uint64_t watchGeneration;
static atomic_uint_fast64_t generation;
static NSString *catalogStorage;

// A Foundation run loop and AVCapture delegate queue live inside Tracker.
// No native helper processes, windows, camera ownership or settings in Manager.
static void initialize(void){static dispatch_once_t once;dispatch_once(&once,^{
 dispatch_semaphore_t ready=dispatch_semaphore_create(0);
 frameQueue=dispatch_queue_create("spatial.camera.frames",DISPATCH_QUEUE_SERIAL);
 [NSThread detachNewThreadWithBlock:^{@autoreleasepool {
  cameraLoop=CFRunLoopGetCurrent();CFRetain(cameraLoop);
  CFRunLoopSourceContext context={0};CFRunLoopSourceRef keepAlive=CFRunLoopSourceCreate(NULL,0,&context);
  CFRunLoopAddSource(cameraLoop,keepAlive,kCFRunLoopDefaultMode);CFRelease(keepAlive);
  dispatch_semaphore_signal(ready);CFRunLoopRun();
 }}];dispatch_semaphore_wait(ready,DISPATCH_TIME_FOREVER);
});}
static void enqueue(dispatch_block_t action){initialize();CFRunLoopPerformBlock(cameraLoop,kCFRunLoopDefaultMode,action);CFRunLoopWakeUp(cameraLoop);}
static void perform(dispatch_block_t action){initialize();if(CFRunLoopGetCurrent()==cameraLoop){action();return;}dispatch_semaphore_t done=dispatch_semaphore_create(0);enqueue(^{@autoreleasepool {action();dispatch_semaphore_signal(done);}});dispatch_semaphore_wait(done,DISPATCH_TIME_FOREVER);}
static NSString *digest(NSString *prefix,NSString *value){NSData *data=[value dataUsingEncoding:NSUTF8StringEncoding];unsigned char hash[CC_SHA256_DIGEST_LENGTH];CC_SHA256(data.bytes,(CC_LONG)data.length,hash);NSMutableString *name=[prefix mutableCopy];for(int i=0;i<16;i++)[name appendFormat:@"%02x",hash[i]];return name;}
static NSString *identity(AVCaptureDevice *device){return digest(@"avfoundation-",device.uniqueID);}
static NSArray *devices(void){return [AVCaptureDevice devicesWithMediaType:AVMediaTypeVideo];}
static NSString *json(id value){NSData *data=[NSJSONSerialization dataWithJSONObject:value options:0 error:NULL];return [[NSString alloc]initWithData:data encoding:NSUTF8StringEncoding];}
static NSDictionary *mode(AVCaptureDevice *device,AVCaptureDeviceFormat *format,AVFrameRateRange *range){
 CMVideoDimensions size=CMVideoFormatDescriptionGetDimensions(format.formatDescription);CMTime duration=range.minFrameDuration;
 if(size.width<=0||size.height<=0||(uint64_t)size.width*size.height>16777216||duration.value<=0||duration.timescale<=0)return nil;
 NSString *key=[NSString stringWithFormat:@"%@|%d|%d|%u|%lld|%d",device.uniqueID,size.width,size.height,CMFormatDescriptionGetMediaSubType(format.formatDescription),duration.value,duration.timescale];
 return @{@"mode_id":digest(@"avfoundation-mode-",key),@"width_px":@(size.width),@"height_px":@(size.height),@"fps":@((double)duration.timescale/duration.value),@"fps_numerator":@(duration.timescale),@"fps_denominator":@(duration.value),@"pixel_format":@"BGRA8"};
}
static NSArray *modes(AVCaptureDevice *device){NSMutableDictionary *unique=[NSMutableDictionary dictionary];for(AVCaptureDeviceFormat *format in device.formats)for(AVFrameRateRange *range in format.videoSupportedFrameRateRanges){NSDictionary *entry=mode(device,format,range);if(entry)unique[entry[@"mode_id"]]=entry;}NSMutableArray *list=[NSMutableArray array];for(NSString *key in [unique.allKeys sortedArrayUsingSelector:@selector(compare:)])[list addObject:unique[key]];return list;}
static uint64_t stamp(void){mach_timebase_info_data_t base;mach_timebase_info(&base);return (uint64_t)((__uint128_t)mach_absolute_time()*base.numer/((uint64_t)base.denom*1000));}
uint64_t SWCameraNow(void){return stamp();}
static void stop(void);
@interface SWCapture:NSObject<AVCaptureVideoDataOutputSampleBufferDelegate>
@property uint64_t nativeGeneration,token;
@property Ready ready;
@property Frame frame;
@property Failure failure;
@property BOOL started;
@property int width,height;
@property NSString *selectedMode;
@property AVCaptureDevice *device;
@end
@implementation SWCapture
-(void)captureOutput:(AVCaptureOutput *)output didOutputSampleBuffer:(CMSampleBufferRef)sample fromConnection:(AVCaptureConnection *)connection {
 @autoreleasepool {
  if(self.nativeGeneration!=atomic_load(&generation)||!self.started)return;
  uint64_t captured=stamp();CVPixelBufferRef buffer=CMSampleBufferGetImageBuffer(sample);if(!buffer)return;
  CVPixelBufferLockBaseAddress(buffer,kCVPixelBufferLock_ReadOnly);
  size_t width=CVPixelBufferGetWidth(buffer),height=CVPixelBufferGetHeight(buffer),stride=CVPixelBufferGetBytesPerRow(buffer);
  if(width==0||height==0||width*height>16777216||stride<width*4||stride*height>134217728||(self.width&&(width!=(size_t)self.width||height!=(size_t)self.height))){CVPixelBufferUnlockBaseAddress(buffer,kCVPixelBufferLock_ReadOnly);return;}
  if(self.ready){
   Ready callback=self.ready;self.ready=NULL;double duration=CMTimeGetSeconds(self.device.activeVideoMinFrameDuration);
   NSString *description=json(@{@"width":@(width),@"height":@(height),@"fps":@(duration>0?1/duration:0),@"mode_id":self.selectedMode?:NSNull.null,@"modes":modes(self.device)});
   callback(self.token,description.UTF8String);
  }
  if(self.nativeGeneration==atomic_load(&generation))self.frame(self.token,captured,(int)width,(int)height,(int)stride,CVPixelBufferGetBaseAddress(buffer));
  CVPixelBufferUnlockBaseAddress(buffer,kCVPixelBufferLock_ReadOnly);
 }
}
-(void)observeValueForKeyPath:(NSString *)keyPath ofObject:(id)object change:(NSDictionary *)change context:(void *)context {
 if([keyPath isEqual:@"inUseByAnotherApplication"]&&self.device.inUseByAnotherApplication){uint64_t expected=self.nativeGeneration;enqueue(^{if(expected==atomic_load(&generation)){self.failure(self.token,"CAMERA_BUSY");stop();}});}
}
@end
static void stop(void){
 atomic_fetch_add(&generation,1);
 SWCapture *old=delegate;
 if(errorObserver){[NSNotificationCenter.defaultCenter removeObserver:errorObserver];errorObserver=nil;}
 if(old.device)[old.device removeObserver:old forKeyPath:@"inUseByAnotherApplication"];
 for(AVCaptureOutput *output in session.outputs)if([output isKindOfClass:AVCaptureVideoDataOutput.class])[(AVCaptureVideoDataOutput *)output setSampleBufferDelegate:nil queue:NULL];
 [session stopRunning];dispatch_sync(frameQueue,^{});session=nil;delegate=nil;
}
const char *SWCameraCatalog(void){perform(^{NSMutableArray *list=[NSMutableArray array];for(AVCaptureDevice *d in devices())[list addObject:@{@"camera_id":identity(d),@"display_name":d.localizedName,@"modes":modes(d)}];catalogStorage=json(list);});return catalogStorage.UTF8String;}
int SWCameraWatch(Changed callback){if(!callback)return 0;__block BOOL good=NO;perform(^{if(changedCallback)return;changedCallback=callback;watchGeneration++;watchObservers=[NSMutableArray array];uint64_t expected=watchGeneration;for(NSString *name in @[AVCaptureDeviceWasConnectedNotification,AVCaptureDeviceWasDisconnectedNotification]){id observer=[NSNotificationCenter.defaultCenter addObserverForName:name object:nil queue:nil usingBlock:^(NSNotification *note){enqueue(^{if(changedCallback&&watchGeneration==expected)changedCallback();});}];[watchObservers addObject:observer];}good=YES;});return good?1:0;}
void SWCameraUnwatch(void){perform(^{changedCallback=NULL;watchGeneration++;for(id observer in watchObservers)[NSNotificationCenter.defaultCenter removeObserver:observer];watchObservers=nil;});}
static void openDevice(NSString *selected,NSString *requested,uint64_t token,uint64_t expected,Ready ready,Frame frame,Failure failure){
 @try {
 if(expected!=atomic_load(&generation))return;
 if([AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo]!=AVAuthorizationStatusAuthorized){failure(token,"PERMISSION_DENIED");return;}
 AVCaptureDevice *device=nil;for(AVCaptureDevice *candidate in devices())if([identity(candidate) isEqual:selected])device=candidate;
 if(!device){failure(token,"CAMERA_UNAVAILABLE");return;}
 if(device.inUseByAnotherApplication){failure(token,"CAMERA_BUSY");return;}
 NSError *error=nil;AVCaptureDeviceInput *input=[AVCaptureDeviceInput deviceInputWithDevice:device error:&error];if(!input){failure(token,error.code==AVErrorDeviceInUseByAnotherApplication?"CAMERA_BUSY":"CAPTURE_FAILED");return;}
 AVCaptureDeviceFormat *format=nil;CMTime duration=kCMTimeInvalid;
 if(requested){for(AVCaptureDeviceFormat *f in device.formats)for(AVFrameRateRange *range in f.videoSupportedFrameRateRanges)if([mode(device,f,range)[@"mode_id"] isEqual:requested]){format=f;duration=range.minFrameDuration;}if(!format){failure(token,"UNSUPPORTED_CAPABILITY");return;}}
 session=[AVCaptureSession new];[session beginConfiguration];
 if(![session canAddInput:input]){failure(token,"CAPTURE_FAILED");session=nil;return;}[session addInput:input];
 if(!requested&&[session canSetSessionPreset:AVCaptureSessionPreset1280x720])session.sessionPreset=AVCaptureSessionPreset1280x720;
 AVCaptureVideoDataOutput *output=[AVCaptureVideoDataOutput new];output.videoSettings=@{(id)kCVPixelBufferPixelFormatTypeKey:@(kCVPixelFormatType_32BGRA)};output.alwaysDiscardsLateVideoFrames=YES;
 if(![session canAddOutput:output]){failure(token,"CAPTURE_FAILED");session=nil;return;}[session addOutput:output];[session commitConfiguration];
 SWCapture *capture=[SWCapture new];capture.nativeGeneration=expected;capture.token=token;capture.ready=ready;capture.frame=frame;capture.failure=failure;capture.selectedMode=requested;capture.device=device;
 delegate=capture;[device addObserver:capture forKeyPath:@"inUseByAnotherApplication" options:0 context:NULL];
 [output setSampleBufferDelegate:capture queue:frameQueue];AVCaptureConnection *connection=[output connectionWithMediaType:AVMediaTypeVideo];if(connection.isVideoMirroringSupported){connection.automaticallyAdjustsVideoMirroring=NO;connection.videoMirrored=NO;}
 errorObserver=[NSNotificationCenter.defaultCenter addObserverForName:AVCaptureSessionRuntimeErrorNotification object:session queue:nil usingBlock:^(NSNotification *note){NSError *e=note.userInfo[AVCaptureSessionErrorKey];enqueue(^{if(expected==atomic_load(&generation)){failure(token,e.code==AVErrorDeviceInUseByAnotherApplication?"CAMERA_BUSY":e.code==AVErrorDeviceWasDisconnected?"CAMERA_UNAVAILABLE":"CAPTURE_FAILED");stop();}});}];
 [session startRunning];
 if(format){
  if(![device lockForConfiguration:&error]){failure(token,"CAMERA_BUSY");stop();return;}
  device.activeFormat=format;device.activeVideoMinFrameDuration=duration;device.activeVideoMaxFrameDuration=duration;
  BOOL applied=device.activeFormat==format&&CMTimeCompare(device.activeVideoMinFrameDuration,duration)==0;[device unlockForConfiguration];
  if(!applied){failure(token,"UNSUPPORTED_CAPABILITY");stop();return;}
  CMVideoDimensions size=CMVideoFormatDescriptionGetDimensions(format.formatDescription);capture.width=size.width;capture.height=size.height;
 }
 // Publish on the delegate queue so configuration is visible before callbacks.
 dispatch_async(frameQueue,^{capture.started=YES;});
 } @catch(NSException *exception){failure(token,"CAPTURE_FAILED");stop();}
}
int SWCameraOpen(const char *selected,const char *requested,uint64_t token,Ready ready,Frame frame,Failure failure){
 if(!selected||!ready||!frame||!failure)return 0;
 NSString *device=[NSString stringWithUTF8String:selected],*modeID=requested?[NSString stringWithUTF8String:requested]:nil;
 enqueue(^{stop();uint64_t expected=atomic_load(&generation);
  if([AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo]==AVAuthorizationStatusNotDetermined){[AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted){enqueue(^{openDevice(device,modeID,token,expected,ready,frame,failure);});}];}
  else openDevice(device,modeID,token,expected,ready,frame,failure);
 });return 1;
}
void SWCameraClose(void){perform(^{stop();});}
