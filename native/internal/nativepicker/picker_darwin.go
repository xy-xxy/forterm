//go:build darwin && cgo

package nativepicker

/*
#cgo CFLAGS: -x objective-c -fobjc-arc
#cgo LDFLAGS: -framework Cocoa
#include <stdint.h>
#include <stdlib.h>
void forterm_picker_start(uintptr_t token, uintptr_t owner, int save, int directory, int multiple, const char *title, const char *initial, const char *name);
void forterm_picker_cancel(uintptr_t token);
*/
import "C"
import (
	"fyne.io/fyne/v2/driver"
	"unsafe"
)

func startPlatform(id uint64, owner any, r Request) error {
	context, ok := owner.(driver.MacWindowContext)
	if !ok || context.NSWindow == 0 {
		return ErrUnavailable
	}
	title, initial, name := C.CString(r.Title), C.CString(r.InitialDirectory), C.CString(r.Name)
	defer C.free(unsafe.Pointer(title))
	defer C.free(unsafe.Pointer(initial))
	defer C.free(unsafe.Pointer(name))
	C.forterm_picker_start(C.uintptr_t(id), C.uintptr_t(context.NSWindow), boolInt(r.Save), boolInt(r.Directory), boolInt(r.Multiple), title, initial, name)
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
