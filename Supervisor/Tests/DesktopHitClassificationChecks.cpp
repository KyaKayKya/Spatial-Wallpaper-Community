// Independent known geometry; the classifier never chooses its own test points.
#define wmain DesktopHitHelperMain
#include "../Interactive/DesktopHitTester.cpp"
#undef wmain
#include <commctrl.h>
#include <cstdio>
#pragma comment(lib,"comctl32.lib")
static bool negative=false;
static bool Check(HWND list,const Desktop& desktop,POINT point,uint32_t expected,const char* name){
 ClientToScreen(list,&point);uint32_t actual=Classify(point,desktop);
 if(negative&&actual==2)actual=1;
 bool ok=actual==expected;std::printf("%s %s expected=%u actual=%u\n",ok?"PASS":"FAIL",name,expected,actual);return ok;
}
static POINT Middle(RECT rectangle){return POINT{(rectangle.left+rectangle.right)/2,(rectangle.top+rectangle.bottom)/2};}
int wmain(int argc,wchar_t** argv){
 negative=argc==2&&!wcscmp(argv[1],L"--negative-icon-as-blank");
 SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
 if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 2;
 INITCOMMONCONTROLSEX controls{sizeof(controls),ICC_LISTVIEW_CLASSES};if(!InitCommonControlsEx(&controls))return 3;
 HWND foreground=GetForegroundWindow();HWND host=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST,L"STATIC",L"SDK owned classification fixture",WS_POPUP,30,30,640,400,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
 HWND list=CreateWindowExW(0,WC_LISTVIEWW,L"",WS_CHILD|WS_VISIBLE|LVS_ICON,0,0,640,400,host,nullptr,GetModuleHandleW(nullptr),nullptr);
 if(!host||!list){if(host)DestroyWindow(host);CoUninitialize();return 4;}
 HIMAGELIST images=ImageList_Create(32,32,ILC_COLOR32|ILC_MASK,1,1);ImageList_AddIcon(images,LoadIconW(nullptr,MAKEINTRESOURCEW(32512)));ListView_SetImageList(list,images,LVSIL_NORMAL);
 for(int i=0;i<2;i++){wchar_t label[]=L"Owned test item";LVITEMW item{};item.mask=LVIF_TEXT|LVIF_IMAGE;item.iItem=i;item.iImage=0;item.pszText=label;SendMessageW(list,LVM_INSERTITEMW,0,reinterpret_cast<LPARAM>(&item));ListView_SetItemPosition(list,i,20+i*200,20);}
 ShowWindow(host,SW_SHOWNOACTIVATE);UpdateWindow(host);Desktop desktop;desktop.list=list;desktop.pid=GetCurrentProcessId();desktop.matches=1;
 bool ok=true;
 for(int selected=0;selected<2;selected++){
  ListView_SetItemState(list,0,selected?LVIS_SELECTED:0,LVIS_SELECTED);UpdateWindow(list);
  for(int i=0;i<2;i++){RECT icon{},label{};if(!ListView_GetItemRect(list,i,&icon,LVIR_ICON)||!ListView_GetItemRect(list,i,&label,LVIR_LABEL)){ok=false;break;}
   ok=Check(list,desktop,Middle(icon),2,selected?"selected-icon":"icon")&&ok;
   ok=Check(list,desktop,Middle(label),2,selected?"selected-label":"label")&&ok;
  }
  ok=Check(list,desktop,POINT{170,35},1,"between-items-gap")&&ok;
  ok=Check(list,desktop,POINT{590,350},1,"empty-client")&&ok;
  ok=Check(list,desktop,POINT{-5,-5},3,"outside-client")&&ok;
 }
 HWND cover=CreateWindowExW(WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE|WS_EX_TOPMOST,L"BUTTON",L"",WS_POPUP,600,350,50,50,host,nullptr,GetModuleHandleW(nullptr),nullptr);ShowWindow(cover,SW_SHOWNOACTIVATE);UpdateWindow(cover);
 POINT covered{590,350};ClientToScreen(list,&covered);bool coverReady=cover&&WindowFromPoint(covered)==cover;std::printf("%s cover-independently-hit\n",coverReady?"PASS":"FAIL");ok=coverReady&&ok;
 ok=Check(list,desktop,POINT{590,350},3,"covered-empty-client")&&ok;DestroyWindow(cover);
 ok=Check(list,desktop,POINT{590,350},1,"uncovered-empty-client")&&ok;
 POINT stale{590,350};ClientToScreen(list,&stale);DestroyWindow(host);uint32_t destroyed=Classify(stale,desktop);ok=(destroyed!=1&&destroyed!=2)&&ok;
 std::printf("%s destroyed-list-rejects actual=%u\n",destroyed!=1&&destroyed!=2?"PASS":"FAIL",destroyed);
 ImageList_Destroy(images);ok=(GetForegroundWindow()==foreground)&&ok;CoUninitialize();return ok?0:1;
}
