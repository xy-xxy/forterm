//go:build windows && cgo
#include <windows.h>
#include <shobjidl.h>
#include <stdint.h>
#include <stdlib.h>
extern void fortermPickerItem(uintptr_t,const char*);
extern void fortermPickerDone(uintptr_t,int);
typedef struct Request{uintptr_t token;HWND owner,control;int save,directory,multiple;volatile LONG cancelled;WCHAR *title,*initial,*name;IFileDialog *dialog;struct Request *next;} Request;
static SRWLOCK lock=SRWLOCK_INIT;static Request *active=NULL;static INIT_ONCE init=INIT_ONCE_STATIC_INIT;
static WCHAR *wide(const char *v){int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,v,-1,NULL,0);if(!n)return NULL;WCHAR *w=calloc(n,sizeof(WCHAR));MultiByteToWideChar(CP_UTF8,0,v,-1,w,n);return w;}
static LRESULT CALLBACK controlProc(HWND w,UINT m,WPARAM a,LPARAM b){if(m==WM_APP+9){Request *r=(Request*)GetWindowLongPtrW(w,GWLP_USERDATA);if(r&&r->token==(uintptr_t)a&&r->dialog)IFileDialog_Close(r->dialog,HRESULT_FROM_WIN32(ERROR_CANCELLED));return 0;}return DefWindowProcW(w,m,a,b);}
static BOOL CALLBACK registerControl(PINIT_ONCE a,PVOID b,PVOID *c){WNDCLASSW wc={0};wc.lpfnWndProc=controlProc;wc.hInstance=GetModuleHandleW(NULL);wc.lpszClassName=L"FortermPickerCancel";return RegisterClassW(&wc)||GetLastError()==ERROR_CLASS_ALREADY_EXISTS;}
static void sendItem(uintptr_t token,IShellItem *item){PWSTR path=NULL;if(SUCCEEDED(IShellItem_GetDisplayName(item,SIGDN_FILESYSPATH,&path))){int n=WideCharToMultiByte(CP_UTF8,0,path,-1,NULL,0,NULL,NULL);char *text=malloc(n);WideCharToMultiByte(CP_UTF8,0,path,-1,text,n,NULL,NULL);fortermPickerItem(token,text);free(text);CoTaskMemFree(path);}}
static DWORD WINAPI pickerThread(void *value){Request *r=value;HRESULT initialized=CoInitializeEx(NULL,COINIT_APARTMENTTHREADED|COINIT_DISABLE_OLE1DDE),hr=initialized;IFileOpenDialog *open=NULL;IFileSaveDialog *save=NULL;int code=2;
 if(SUCCEEDED(hr)){InitOnceExecuteOnce(&init,registerControl,NULL,NULL);HWND control=CreateWindowExW(0,L"FortermPickerCancel",L"",0,0,0,0,0,HWND_MESSAGE,NULL,GetModuleHandleW(NULL),NULL);SetWindowLongPtrW(control,GWLP_USERDATA,(LONG_PTR)r);AcquireSRWLockExclusive(&lock);r->control=control;ReleaseSRWLockExclusive(&lock);
  if(r->save){hr=CoCreateInstance(&CLSID_FileSaveDialog,NULL,CLSCTX_INPROC_SERVER,&IID_IFileSaveDialog,(void**)&save);r->dialog=(IFileDialog*)save;}else{hr=CoCreateInstance(&CLSID_FileOpenDialog,NULL,CLSCTX_INPROC_SERVER,&IID_IFileOpenDialog,(void**)&open);r->dialog=(IFileDialog*)open;}
  if(SUCCEEDED(hr)){FILEOPENDIALOGOPTIONS options=FOS_FORCEFILESYSTEM|FOS_PATHMUSTEXIST|FOS_NOCHANGEDIR|FOS_NODEREFERENCELINKS;if(r->save)options|=FOS_OVERWRITEPROMPT;else options|=FOS_FILEMUSTEXIST;if(r->directory)options|=FOS_PICKFOLDERS;if(r->multiple&&!r->save)options|=FOS_ALLOWMULTISELECT;IFileDialog_SetOptions(r->dialog,options);if(r->title)IFileDialog_SetTitle(r->dialog,r->title);if(r->save&&r->name)IFileDialog_SetFileName(r->dialog,r->name);
   if(r->initial&&r->initial[0]){IShellItem *folder=NULL;if(SUCCEEDED(SHCreateItemFromParsingName(r->initial,NULL,&IID_IShellItem,(void**)&folder))){IFileDialog_SetFolder(r->dialog,folder);IShellItem_Release(folder);}}
   if(InterlockedCompareExchange(&r->cancelled,0,0))hr=HRESULT_FROM_WIN32(ERROR_CANCELLED);else hr=IFileDialog_Show(r->dialog,r->owner);
   if(hr==HRESULT_FROM_WIN32(ERROR_CANCELLED))code=1;else if(SUCCEEDED(hr)){code=0;if(open){IShellItemArray *items=NULL;if(SUCCEEDED(IFileOpenDialog_GetResults(open,&items))){DWORD count=0;IShellItemArray_GetCount(items,&count);for(DWORD i=0;i<count&&i<1001;i++){IShellItem *item=NULL;if(SUCCEEDED(IShellItemArray_GetItemAt(items,i,&item))){sendItem(r->token,item);IShellItem_Release(item);}};IShellItemArray_Release(items);}}else{IShellItem *item=NULL;if(SUCCEEDED(IFileDialog_GetResult(r->dialog,&item))){sendItem(r->token,item);IShellItem_Release(item);}}}
  }
  IFileDialog *completed=r->dialog;r->dialog=NULL;if(completed)IFileDialog_Release(completed);if(r->control)DestroyWindow(r->control);CoUninitialize();
 }
 AcquireSRWLockExclusive(&lock);Request **p=&active;while(*p&&*p!=r)p=&(*p)->next;if(*p)*p=r->next;ReleaseSRWLockExclusive(&lock);
 fortermPickerDone(r->token,code);free(r->title);free(r->initial);free(r->name);free(r);return 0;
}
int forterm_picker_start(uintptr_t token,uintptr_t owner,int save,int directory,int multiple,const char *title,const char *initial,const char *name){Request *r=calloc(1,sizeof(Request));if(!r)return 0;r->token=token;r->owner=(HWND)owner;r->save=save;r->directory=directory;r->multiple=multiple;r->title=wide(title);r->initial=wide(initial);r->name=wide(name);AcquireSRWLockExclusive(&lock);r->next=active;active=r;ReleaseSRWLockExclusive(&lock);HANDLE h=CreateThread(NULL,0,pickerThread,r,0,NULL);if(!h){AcquireSRWLockExclusive(&lock);Request **p=&active;while(*p&&*p!=r)p=&(*p)->next;if(*p)*p=r->next;ReleaseSRWLockExclusive(&lock);free(r->title);free(r->initial);free(r->name);free(r);return 0;}CloseHandle(h);return 1;}
void forterm_picker_cancel(uintptr_t token){AcquireSRWLockShared(&lock);for(Request *r=active;r;r=r->next){if(r->token==token){InterlockedExchange(&r->cancelled,1);if(r->control)PostMessageW(r->control,WM_APP+9,(WPARAM)token,0);break;}}ReleaseSRWLockShared(&lock);}
