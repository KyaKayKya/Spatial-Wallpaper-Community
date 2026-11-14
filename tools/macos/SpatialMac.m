#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>

// Main-thread-only ABI. Strings are borrowed until the next call of that function.
static NSWindow *renderWindow;
static NSScreen *target;
static NSWindowStyleMask originalStyle;
static NSWindowCollectionBehavior originalBehavior;
static NSInteger originalLevel;
static BOOL originalMouse;
static NSRect originalFrame;
static NSString *targetID;
static BOOL attached;
static void (*dispatchEvent)(const char *);
static NSMutableArray *observers;
static NSStatusItem *statusItem;
static NSString *displayID(NSScreen *screen) {
 CGDirectDisplayID n=[screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue];
 CFUUIDRef uuid=CGDisplayCreateUUIDFromDisplayID(n);
 if(!uuid)return nil;
 NSString *text=CFBridgingRelease(CFUUIDCreateString(NULL,uuid));CFRelease(uuid);
 return [@"mac-display-" stringByAppendingString:text];
}
static NSScreen *findScreen(NSString *identity){for(NSScreen *s in NSScreen.screens)if([displayID(s) isEqual:identity])return s;return nil;}
static BOOL unityView(NSView *view){if([NSStringFromClass(view.class) containsString:@"Unity"])return YES;for(NSView *child in view.subviews)if(unityView(child))return YES;return NO;}
static NSWindow *findWindow(){for(NSWindow *w in NSApp.windows)if(w.contentView&&unityView(w.contentView))return w;return nil;}
static const char *json(id value,NSString * __strong *storage){NSData *d=[NSJSONSerialization dataWithJSONObject:value options:0 error:NULL];*storage=[[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding];return [*storage UTF8String];}
int SWPrepareStartup(int visible){
 if(!NSThread.isMainThread)return 0;NSWindow *window=findWindow();if(!window)return 0;
 if(!visible){[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];[window orderOut:nil];}
 return 1;
}
int SWInitialize(void){
 if(!NSThread.isMainThread)return 0;
 if(!renderWindow)renderWindow=findWindow();
 if(!renderWindow)return 0;
 [renderWindow orderOut:nil];return 1;
}
void SWHide(void){if(NSThread.isMainThread)[renderWindow orderOut:nil];}
void SWDetach(void){
 if(!NSThread.isMainThread||!attached)return;
 [renderWindow orderOut:nil];renderWindow.styleMask=originalStyle;renderWindow.collectionBehavior=originalBehavior;renderWindow.level=originalLevel;renderWindow.ignoresMouseEvents=originalMouse;[renderWindow setFrame:originalFrame display:NO];attached=NO;targetID=nil;target=nil;
}
int SWVerify(void){
 if(!NSThread.isMainThread||!attached||!renderWindow.visible||!renderWindow.ignoresMouseEvents)return 0;
 NSScreen *current=findScreen(targetID);if(!current)return 0;
 NSRect client=[renderWindow.contentView convertRect:renderWindow.contentView.bounds toView:nil];client=[renderWindow convertRectToScreen:client];
 return NSEqualRects(current.frame,client)&&renderWindow.level==CGWindowLevelForKey(kCGDesktopWindowLevelKey)+1&&renderWindow.styleMask==NSWindowStyleMaskBorderless;
}
int SWAttach(const char *identity){
 if(!NSThread.isMainThread||!renderWindow||!identity)return 0;
 NSScreen *screen=findScreen([NSString stringWithUTF8String:identity]);if(!screen)return 0;
 if(!attached){originalStyle=renderWindow.styleMask;originalLevel=renderWindow.level;originalBehavior=renderWindow.collectionBehavior;originalMouse=renderWindow.ignoresMouseEvents;originalFrame=renderWindow.frame;attached=YES;}
 target=screen;targetID=displayID(screen);
 renderWindow.styleMask=NSWindowStyleMaskBorderless;
 renderWindow.level=CGWindowLevelForKey(kCGDesktopWindowLevelKey)+1;
 renderWindow.collectionBehavior=NSWindowCollectionBehaviorCanJoinAllSpaces|NSWindowCollectionBehaviorStationary|NSWindowCollectionBehaviorIgnoresCycle;
 renderWindow.ignoresMouseEvents=YES;renderWindow.hidesOnDeactivate=NO;renderWindow.animationBehavior=NSWindowAnimationBehaviorNone;
 [renderWindow setFrame:screen.frame display:YES];[renderWindow orderBack:nil];
 if(!SWVerify()){SWDetach();return 0;}return 1;
}
const char *SWDisplays(void){
 static NSString *storage;NSMutableArray *list=[NSMutableArray array];
 for(NSScreen *s in NSScreen.screens){NSRect r=s.frame;CGFloat scale=s.backingScaleFactor;NSString *identity=displayID(s);if(!identity)continue;
  [list addObject:@{@"id":identity,@"name":s.localizedName,@"x":@(lround(r.origin.x*scale)),@"y":@(lround(-NSMaxY(r)*scale)),@"width":@(lround(r.size.width*scale)),@"height":@(lround(r.size.height*scale)),@"scale":@(scale)}];
 }return json(list,&storage);
}
const char *SWViewport(void){
 static NSString *storage;NSWindow *w=renderWindow?:findWindow();
 if(!NSThread.isMainThread||!w||!w.visible||w.miniaturized||!w.screen)return json(@{@"valid":@NO},&storage);
 NSScreen *s=w.screen;NSRect m=s.frame;CGFloat scale=s.backingScaleFactor;
 NSRect r=[w convertRectToScreen:[w.contentView convertRect:w.contentView.bounds toView:nil]];
 return json(@{@"valid":@YES,@"id":displayID(s),@"mx":@(m.origin.x*scale),@"my":@(-NSMaxY(m)*scale),@"mw":@(m.size.width*scale),@"mh":@(m.size.height*scale),@"x":@(r.origin.x*scale),@"y":@(-NSMaxY(r)*scale),@"width":@(r.size.width*scale),@"height":@(r.size.height*scale)},&storage);
}
int SWActivateManager(int owner){
 NSRunningApplication *app=[NSRunningApplication runningApplicationWithProcessIdentifier:owner];
 NSString *selfPath=NSRunningApplication.currentApplication.executableURL.path;
 if(!app||![app.executableURL.path isEqual:selfPath])return 0;
 return [app activateWithOptions:NSApplicationActivateIgnoringOtherApps];
}
int SWCovered(void){
 // Public on-screen window bounds; does not inspect titles or pixels.
 if(!attached||!target)return 0;
 pid_t front=NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier;
 if(front==getpid())return 0;
 CGDirectDisplayID n=[target.deviceDescription[@"NSScreenNumber"] unsignedIntValue];CGRect bounds=CGDisplayBounds(n);
 NSArray *windows=CFBridgingRelease(CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly|kCGWindowListExcludeDesktopElements,kCGNullWindowID));
 for(NSDictionary *item in windows){if([item[(id)kCGWindowOwnerPID] intValue]!=front||[item[(id)kCGWindowLayer] intValue]!=0)continue;CGRect r;if(CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)item[(id)kCGWindowBounds],&r)&&CGRectContainsRect(r,bounds))return 1;}
 return 0;
}
@interface SWMenuTarget:NSObject
-(void)command:(NSMenuItem *)item;
@end
@implementation SWMenuTarget
-(void)command:(NSMenuItem *)item {if(dispatchEvent)dispatchEvent([item.representedObject UTF8String]);}
@end
static SWMenuTarget *menuTarget;
int SWStartShell(void (*callback)(const char *)){
 if(!NSThread.isMainThread||!callback||dispatchEvent)return 0;dispatchEvent=callback;observers=[NSMutableArray array];menuTarget=[SWMenuTarget new];
 statusItem=[NSStatusBar.systemStatusBar statusItemWithLength:NSVariableStatusItemLength];statusItem.button.title=@"SW";
 NSMenu *menu=[NSMenu new];
 NSArray *labels=@[@"打开管理器",@"暂停／继续",@"停止空间壁纸",@"完全退出"];
 NSArray *commands=@[@"open",@"pause",@"stop",@"quit"];
 for(NSUInteger i=0;i<commands.count;i++){NSMenuItem *item=[[NSMenuItem alloc]initWithTitle:labels[i] action:@selector(command:) keyEquivalent:@""];item.target=menuTarget;item.representedObject=commands[i];[menu addItem:item];}statusItem.menu=menu;
 NSArray *names=@[NSWorkspaceWillSleepNotification,NSWorkspaceDidWakeNotification,NSWorkspaceSessionDidResignActiveNotification,NSWorkspaceSessionDidBecomeActiveNotification,NSWorkspaceScreensDidSleepNotification,NSWorkspaceScreensDidWakeNotification];
 NSArray *events=@[@"suspended",@"resumed",@"locked",@"unlocked",@"locked",@"unlocked"];
 NSNotificationCenter *center=NSWorkspace.sharedWorkspace.notificationCenter;
 for(NSUInteger i=0;i<names.count;i++){NSString *event=events[i];id observer=[center addObserverForName:names[i] object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){if(dispatchEvent)dispatchEvent(event.UTF8String);}];[observers addObject:@[center,observer]];}
 id observer=[NSNotificationCenter.defaultCenter addObserverForName:NSApplicationDidChangeScreenParametersNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note){if(dispatchEvent)dispatchEvent("display-changed");}];[observers addObject:@[NSNotificationCenter.defaultCenter,observer]];
 return 1;
}
void SWStopShell(void){if(!NSThread.isMainThread)return;dispatchEvent=NULL;for(NSArray *pair in observers)[pair[0] removeObserver:pair[1]];[observers removeAllObjects];if(statusItem)[NSStatusBar.systemStatusBar removeStatusItem:statusItem];statusItem=nil;menuTarget=nil;}
