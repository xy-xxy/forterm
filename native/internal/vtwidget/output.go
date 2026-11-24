package vtwidget

import (
	"fyne.io/fyne/v2"
	"io"
	"time"
)

const outputBlockBytes = 4096
const outputBatchBlocks = 8
const outputFrameInterval = time.Second / 60
const outputParseBudget = 4 * time.Millisecond

type outputBlock struct {
	data []byte
	err  error
}

// A bounded read-ahead queue absorbs small SSH reads. Only one UI callback is
// outstanding, at most 32KiB is parsed per frame, and all accepted bytes remain
// ordered. Backpressure stops reading instead of dropping or growing memory.
func (t *Terminal) consumeOutput(out io.Reader) error {
	blocks := make(chan outputBlock, outputBatchBlocks)
	go func() {
		defer close(blocks)
		buf := make([]byte, outputBlockBytes)
		for {
			n, err := out.Read(buf)
			t.outputRead.Add(uint64(n))
			if n > 0 || err != nil {
				block := outputBlock{data: append([]byte(nil), buf[:n]...), err: err}
				select {
				case blocks <- block:
				case <-t.done:
					return
				}
			}
			if err != nil {
				return
			}
			if t.stopped() {
				return
			}
		}
	}()
	next := time.Time{}
	var pending *outputBlock
	for {
		var block outputBlock
		var ok bool
		resuming := pending != nil
		if resuming {
			block = *pending
			pending = nil
		} else {
			select {
			case <-t.done:
				return nil
			case block, ok = <-blocks:
				if !ok {
					return nil
				}
			}
		}
		if wait := time.Until(next); wait > 0 {
			timer := time.NewTimer(wait)
			select {
			case <-timer.C:
			case <-t.done:
				timer.Stop()
				return nil
			}
		}
		batch := block.data
		lastErr := block.err
		for i := 1; !resuming && i < outputBatchBlocks && lastErr == nil; i++ {
			select {
			case extra, open := <-blocks:
				if !open {
					i = outputBatchBlocks
					break
				}
				batch = append(batch, extra.data...)
				lastErr = extra.err
			default:
				i = outputBatchBlocks
			}
		}
		consumed := 0
		fyne.DoAndWait(func() {
			if t.stopped() {
				return
			}
			if len(batch) > 0 {
				t.start, t.end = nil, nil
				start := time.Now()
				for consumed < len(batch) {
					end := min(len(batch), consumed+outputBlockBytes)
					_, _ = t.model.Write(batch[consumed:end])
					consumed = end
					if time.Since(start) >= outputParseBudget {
						break
					}
				}
				t.Refresh()
				t.outputRendered.Add(uint64(consumed))
				t.outputBatches.Add(1)
			}
			if lastErr != nil && consumed == len(batch) {
				t.Close()
			}
		})
		if consumed < len(batch) {
			pending = &outputBlock{data: batch[consumed:], err: lastErr}
		}
		if (lastErr != nil && pending == nil) || t.stopped() {
			return nil
		}
		next = time.Now().Add(outputFrameInterval)
	}
}

// OutputStats reports byte and frame counts only. Rendered bytes are published
// after the corresponding model update and renderer refresh have completed.
func (t *Terminal) OutputStats() (read, rendered, batches uint64) {
	return t.outputRead.Load(), t.outputRendered.Load(), t.outputBatches.Load()
}
