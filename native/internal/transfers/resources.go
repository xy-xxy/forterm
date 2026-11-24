package transfers

import (
	"context"
	"fmt"
	"sort"
	"sync"
)

type resourceEntry struct {
	lane  chan struct{}
	users int
}

var resources = struct {
	sync.Mutex
	lanes map[string]*resourceEntry
}{lanes: map[string]*resourceEntry{}}

type resourceContextKey struct{}

func ResourceKey(connection any) string { return fmt.Sprintf("%T:%p", connection, connection) }
func resourceDone(key string, entry *resourceEntry) {
	resources.Lock()
	entry.users--
	if entry.users == 0 && resources.lanes[key] == entry {
		delete(resources.lanes, key)
	}
	resources.Unlock()
}
func AcquireResources(ctx context.Context, keys ...string) (context.Context, func(), error) {
	if e := ctx.Err(); e != nil {
		return ctx, func() {}, e
	}
	held, _ := ctx.Value(resourceContextKey{}).(map[string]bool)
	unique := map[string]bool{}
	for _, key := range keys {
		if key != "" && !held[key] {
			unique[key] = true
		}
	}
	ordered := make([]string, 0, len(unique))
	for key := range unique {
		ordered = append(ordered, key)
	}
	sort.Strings(ordered)
	type lease struct {
		key   string
		entry *resourceEntry
	}
	acquired := []lease{}
	var once sync.Once
	release := func() {
		once.Do(func() {
			for i := len(acquired) - 1; i >= 0; i-- {
				v := acquired[i]
				<-v.entry.lane
				resourceDone(v.key, v.entry)
			}
		})
	}
	for _, key := range ordered {
		resources.Lock()
		entry := resources.lanes[key]
		if entry == nil {
			entry = &resourceEntry{lane: make(chan struct{}, 1)}
			resources.lanes[key] = entry
		}
		entry.users++
		resources.Unlock()
		select {
		case entry.lane <- struct{}{}:
			acquired = append(acquired, lease{key, entry})
		case <-ctx.Done():
			resourceDone(key, entry)
			release()
			return ctx, func() {}, ctx.Err()
		}
	}
	next := map[string]bool{}
	for key, value := range held {
		next[key] = value
	}
	for key := range unique {
		next[key] = true
	}
	return context.WithValue(ctx, resourceContextKey{}, next), release, nil
}
