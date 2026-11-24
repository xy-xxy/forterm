package fileops

import (
	"context"
	"errors"
	"fmt"
	"forterm/native/internal/remotefs"
	"forterm/native/internal/transfers"
	"os"
	"path"
	"sort"
	"time"
)

type PermissionItem struct {
	Path      string
	Before    Identity
	Directory bool
	Mode      uint32
	Skip      bool
}
type PermissionPlan struct {
	Items     []PermissionItem
	Recursive bool
	Root      string
	Skipped   int
}
type PrivilegeStat struct {
	Device     uint64 `json:"device"`
	Inode      uint64 `json:"inode"`
	Size       int64  `json:"size"`
	Mode       uint32 `json:"mode"`
	ModifiedNS int64  `json:"modifiedNS"`
	Directory  bool   `json:"directory"`
}
type PrivilegedPermissions interface {
	InspectPrivilege(context.Context, PermissionItem) (PrivilegeStat, string, error)
	ApplyPrivilege(context.Context, PermissionItem, PrivilegeStat, func(context.Context) ([]byte, error)) error
}

func PlanPermissions(parent context.Context, fs remotefs.Operations, root string, recursive bool, directoryMode uint32, fileMode *uint32) (PermissionPlan, error) {
	plan := PermissionPlan{Root: root, Recursive: recursive}
	if _, e := SafePath(root); e != nil {
		return plan, e
	}
	if directoryMode > 0777 || fileMode != nil && *fileMode > 0777 {
		return plan, errors.New("普通权限须000–777；特殊位从各原文件保留")
	}
	ctx, cancel := context.WithTimeout(parent, 60*time.Second)
	defer cancel()
	if e := CheckParents(ctx, fs, root); e != nil {
		return plan, e
	}
	seen := map[string]bool{}
	var walk func(string, int) error
	walk = func(name string, depth int) error {
		if ctx.Err() != nil {
			return ctx.Err()
		}
		if depth > 32 || len(seen) >= 2000 {
			return errors.New("权限计划超过2000项或32层，未执行任何变更")
		}
		if seen[name] {
			return errors.New("目录计划发现重复路径，已停止")
		}
		seen[name] = true
		info, e := fs.Stat(ctx, name)
		if e != nil {
			return e
		}
		known := true
		if k, ok := info.(interface{ ModeKnown() bool }); ok {
			known = k.ModeKnown()
		}
		if !known {
			return errors.New("存在未知权限项，不能按000生成计划")
		}
		if info.Mode()&os.ModeSymlink != 0 {
			if name == root {
				return errors.New("不修改链接目标")
			}
			plan.Skipped++
			return nil
		}
		if !info.IsDir() && !info.Mode().IsRegular() {
			plan.Skipped++
			return nil
		}
		if info.IsDir() && recursive {
			entries, e := fs.List(ctx, name)
			if e != nil {
				return e
			}
			sort.Slice(entries, func(i, j int) bool { return entries[i].Name < entries[j].Name })
			for _, entry := range entries {
				if e := ChildName(entry.Name); e != nil {
					return e
				}
				if e := walk(path.Join(name, entry.Name), depth+1); e != nil {
					return e
				}
			}
		}
		normal := directoryMode
		skip := false
		if !info.IsDir() {
			if fileMode == nil {
				if name != root {
					skip = true
				} else {
					normal = directoryMode
				}
			} else {
				normal = *fileMode
			}
		}
		plan.Items = append(plan.Items, PermissionItem{Path: name, Before: IdentityOf(info), Directory: info.IsDir(), Mode: RawMode(info.Mode())&07000 | normal, Skip: skip})
		return nil
	}
	err := walk(root, 0)
	return plan, err
}
func PermissionItemMatches(item PermissionItem, info os.FileInfo) bool {
	return info != nil && info.IsDir() == item.Directory && info.Mode()&os.ModeSymlink == 0 && info.Size() == item.Before.Size && info.ModTime().Equal(item.Before.Modified) && info.Mode() == item.Before.Mode
}
func ApplyPermissionPlan(parent context.Context, fs remotefs.Operations, plan PermissionPlan, useSudo bool, confirmed map[string]PrivilegeStat, password func(context.Context) ([]byte, error), progress func(int, int, string)) error {
	ctx, release, e := transfers.AcquireResources(parent, transfers.ResourceKey(fs))
	if e != nil {
		return e
	}
	defer release()
	ctx, cancel := context.WithTimeout(ctx, 2*time.Minute)
	defer cancel()
	done := 0
	total := 0
	for _, item := range plan.Items {
		if !item.Skip {
			total++
		}
	}
	partial := func(err error) error {
		return fmt.Errorf("已确认完成%d项；剩余未执行，已完成不会回滚：%w", done, err)
	}
	for _, item := range plan.Items {
		if item.Skip {
			continue
		}
		if e = ctx.Err(); e != nil {
			return partial(e)
		}
		if e = CheckParents(ctx, fs, item.Path); e != nil {
			return partial(e)
		}
		info, err := fs.Stat(ctx, item.Path)
		if err != nil || !PermissionItemMatches(item, info) {
			return partial(errors.New("计划后文件类型、内容元数据或权限已变化，请重新预览"))
		}
		if useSudo {
			privileged, ok := fs.(PrivilegedPermissions)
			if !ok {
				return partial(errors.New("该来源不支持同SSH安全sudo"))
			}
			snapshot, ok := confirmed[item.Path]
			if !ok {
				return partial(errors.New("缺少已确认的sudo目标身份"))
			}
			err = privileged.ApplyPrivilege(ctx, item, snapshot, password)
		} else {
			if !fs.ChmodSupported() {
				return partial(errors.New("服务端未确认权限修改能力"))
			}
			err = fs.Mutate(ctx, "chmod", item.Path, "", FileMode(item.Mode))
		}
		if err != nil {
			return partial(err)
		}
		after, err := fs.Stat(ctx, item.Path)
		if err != nil || after == nil || RawMode(after.Mode()) != item.Mode {
			return partial(errors.New("变更后权限未能重新读取确认，请核对当前项"))
		}
		done++
		if progress != nil {
			progress(done, total, item.Path)
		}
	}
	return nil
}
