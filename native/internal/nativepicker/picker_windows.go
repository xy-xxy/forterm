//go:build windows && cgo

package nativepicker

/*
#cgo CFLAGS: -DCOBJMACROS
#cgo LDFLAGS: -lole32 -luuid -lshell32 -lcomdlg32
#include <stdint.h>
#include <stdlib.h>
int forterm_picker_start(uintptr_t token,uintptr_t owner,int save,int directory,int multiple,const char *title,const char *initial,const char *name);
void forterm_picker_cancel(uintptr_t token);
*/
import "C"
import (
	"fyne.io/fyne/v2/driver"
	"unsafe"
)

func startPlatform(id uint64, owner any, r Request) error {
	context, ok := owner.(driver.WindowsWindowContext)
	if !ok || context.HWND == 0 {
		return ErrUnavailable
	}
	t, d, n := C.CString(r.Title), C.CString(r.InitialDirectory), C.CString(r.Name)
	defer C.free(unsafe.Pointer(t))
	defer C.free(unsafe.Pointer(d))
	defer C.free(unsafe.Pointer(n))
	if C.forterm_picker_start(C.uintptr_t(id), C.uintptr_t(context.HWND), boolInt(r.Save), boolInt(r.Directory), boolInt(r.Multiple), t, d, n) == 0 {
		return ErrUnavailable
	}
	return nil
}
func cancelPlatform(id uint64) { C.forterm_picker_cancel(C.uintptr_t(id)) }
func boolInt(v bool) C.int {
	if v {
		return 1
	}
	return 0
}

//export fortermPickerItem
func fortermPickerItem(token C.uintptr_t, path *C.char) { addPath(uint64(token), C.GoString(path)) }

//export fortermPickerDone
func fortermPickerDone(token C.uintptr_t, code C.int) { finish(uint64(token), int(code)) }
