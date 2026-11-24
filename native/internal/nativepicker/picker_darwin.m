//go:build darwin && cgo
#import <Cocoa/Cocoa.h>
#include <stdint.h>
extern void fortermPickerItem(uintptr_t token, const char *path);
extern void fortermPickerDone(uintptr_t token, int code);
static NSMutableDictionary<NSNumber*,NSSavePanel*> *fortermPanels;
void forterm_picker_start(uintptr_t token,uintptr_t owner,int save,int directory,int multiple,const char *title,const char *initial,const char *name){
 NSString *t=[NSString stringWithUTF8String:title],*d=[NSString stringWithUTF8String:initial],*n=[NSString stringWithUTF8String:name];
 NSWindow *parent=(__bridge NSWindow *)(void*)owner;
 dispatch_async(dispatch_get_main_queue(),^{
  if(!fortermPanels)fortermPanels=[NSMutableDictionary dictionary];
  if(!parent||!parent.visible){fortermPickerDone(token,2);return;}
  NSSavePanel *panel;
  if(save){panel=[NSSavePanel savePanel];panel.nameFieldStringValue=n?:@"";}else{NSOpenPanel *open=[NSOpenPanel openPanel];open.canChooseFiles=!directory;open.canChooseDirectories=directory;open.allowsMultipleSelection=multiple;open.resolvesAliases=NO;panel=open;}
  panel.title=t?:@"";panel.canCreateDirectories=save;panel.showsHiddenFiles=NO;
  if(d.length)panel.directoryURL=[NSURL fileURLWithPath:d isDirectory:YES];
  fortermPanels[@(token)]=panel;
  [panel beginSheetModalForWindow:parent completionHandler:^(NSModalResponse response){
   if(response==NSModalResponseOK){NSArray<NSURL*> *urls=save?@[panel.URL]:[(NSOpenPanel*)panel URLs];for(NSURL *url in urls){if(url.isFileURL)fortermPickerItem(token,url.path.UTF8String);}}
   [fortermPanels removeObjectForKey:@(token)];fortermPickerDone(token,response==NSModalResponseOK?0:1);
  }];
 });
}
void forterm_picker_cancel(uintptr_t token){dispatch_async(dispatch_get_main_queue(),^{NSSavePanel *panel=fortermPanels[@(token)];if(panel)[panel cancel:nil];});}
