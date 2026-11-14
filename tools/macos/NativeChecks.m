#import "SpatialMac.m"
#import <assert.h>
@interface UnityFixtureView:NSView
@end
@implementation UnityFixtureView
@end
static NSString *lastEvent;
static void event(const char *message){lastEvent=@(message);}
int main(void){@autoreleasepool {
 [NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
 assert(SWPrepareStartup(0)==0);assert(SWAttach("missing")==0);assert(SWVerify()==0);
 NSWindow *fixture=[[NSWindow alloc]initWithContentRect:NSMakeRect(20,30,320,200) styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
 fixture.contentView=[[UnityFixtureView alloc]initWithFrame:NSMakeRect(0,0,320,200)];
 assert(SWPrepareStartup(0)==1);assert(SWInitialize()==1);assert(!fixture.visible);
 assert(SWAttach("missing")==0);assert(SWVerify()==0);
 NSScreen *screen=NSScreen.screens.firstObject;assert(screen);
 assert(SWAttach(displayID(screen).UTF8String)==1);assert(SWVerify()==1);assert(!fixture.keyWindow);
 NSDictionary *viewport=[NSJSONSerialization JSONObjectWithData:[@(SWViewport()) dataUsingEncoding:NSUTF8StringEncoding] options:0 error:NULL];
 assert([viewport[@"valid"] boolValue]);assert([viewport[@"id"] isEqual:displayID(screen)]);assert([viewport[@"width"] doubleValue]==screen.frame.size.width*screen.backingScaleFactor);
 SWHide();assert(SWVerify()==0);assert(SWAttach(displayID(screen).UTF8String)==1);assert(SWVerify()==1);
 SWDetach();assert(!fixture.visible);assert(fixture.styleMask==NSWindowStyleMaskTitled);assert(!fixture.ignoresMouseEvents);
 assert(SWStartShell(event)==1);[NSWorkspace.sharedWorkspace.notificationCenter postNotificationName:NSWorkspaceWillSleepNotification object:nil];
 assert([lastEvent isEqual:@"suspended"]);SWStopShell();
 [fixture close];puts("PASS native attach/verify/hide/reattach/detach, invalid target, Retina geometry, no key focus, shell event");return 0;
}}
