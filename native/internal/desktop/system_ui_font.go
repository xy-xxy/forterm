package desktop

import (
	"bytes"
	"encoding/binary"
	"errors"
	"io"
	"math"
	"os"
	"path/filepath"
	"runtime"
	"strings"

	"fyne.io/fyne/v2"
	textfont "github.com/go-text/typesetting/font"
	"github.com/go-text/typesetting/font/opentype"
)

var uiSansRegular, uiSansBold fyne.Resource
var uiFontFamily, uiBoldFamily, uiFontSource string

func systemSansCandidates(platform, root string) []string {
	switch platform {
	case "windows":
		return []string{filepath.Join(root, "Fonts", "msyh.ttc"), filepath.Join(root, "Fonts", "msyhbd.ttc"), filepath.Join(root, "Fonts", "simhei.ttf"), filepath.Join(root, "Fonts", "msjh.ttc")}
	case "darwin":
		paths := []string{"/System/Library/Fonts/PingFang.ttc"}
		assets, _ := filepath.Glob("/System/Library/AssetsV2/com_apple_MobileAsset_Font*/*.asset/AssetData/PingFang.ttc")
		paths = append(paths, assets...)
		return append(paths, "/System/Library/Fonts/Hiragino Sans GB.ttc", "/System/Library/Fonts/STHeiti Medium.ttc", "/System/Library/Fonts/STHeiti Light.ttc")
	default:
		return []string{"/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", "/usr/share/fonts/truetype/noto/NotoSansSC-Regular.ttf"}
	}
}

func loadSystemUISans(root string) {
	for _, path := range systemSansCandidates(runtime.GOOS, root) {
		file, err := os.Open(path)
		if err != nil {
			continue
		}
		data, err := io.ReadAll(io.LimitReader(file, 128<<20+1))
		file.Close()
		if err != nil || len(data) > 128<<20 {
			continue
		}
		regular, bold, family, boldFamily, err := selectSystemSansFaces(data)
		if err != nil {
			continue
		}
		uiSansRegular = fyne.NewStaticResource("system-ui-sans-regular.ttf", regular)
		uiSansBold = fyne.NewStaticResource("system-ui-sans-bold.ttf", bold)
		uiFontFamily, uiBoldFamily, uiFontSource = family, boldFamily, filepath.Base(path)
		return
	}
}

func selectSystemSansFaces(data []byte) ([]byte, []byte, string, string, error) {
	loaders, err := opentype.NewLoaders(bytes.NewReader(data))
	if err != nil {
		return nil, nil, "", "", err
	}
	regular, bold := -1, -1
	regularScore, boldScore := math.MaxFloat64, math.MaxFloat64
	descriptions := make([]textfont.Description, len(loaders))
	for i, loader := range loaders {
		desc, _ := textfont.Describe(loader, nil)
		descriptions[i] = desc
		family := strings.ToLower(desc.Family)
		if !strings.Contains(family, "sans") && !strings.Contains(family, "hei") && !strings.Contains(family, "pingfang") {
			continue
		}
		penalty := float64(0)
		if strings.Contains(family, "pingfang") && !strings.Contains(family, " sc") || strings.Contains(family, "heititc") || strings.Contains(family, " tc") || strings.Contains(family, " hk") || strings.Contains(family, "jheng") {
			penalty = 1000
		}
		if score := math.Abs(float64(desc.Aspect.Weight)-400) + penalty; score < regularScore {
			regular, regularScore = i, score
		}
		if score := math.Abs(float64(desc.Aspect.Weight)-700) + penalty; score < boldScore {
			bold, boldScore = i, score
		}
	}
	if regular < 0 {
		return nil, nil, "", "", errors.New("no CJK sans face")
	}
	r, err := standaloneFontFace(data, regular)
	if err != nil {
		return nil, nil, "", "", err
	}
	face, err := textfont.ParseTTF(bytes.NewReader(r))
	if err != nil {
		return nil, nil, "", "", err
	}
	for _, ch := range []rune("中文连接管理A0") {
		if _, ok := face.NominalGlyph(ch); !ok {
			return nil, nil, "", "", errors.New("font lacks required UI glyphs")
		}
	}
	b := r
	if bold != regular {
		b, err = standaloneFontFace(data, bold)
		if err != nil {
			return nil, nil, "", "", err
		}
	}
	return r, b, descriptions[regular].Family, descriptions[bold].Family, nil
}

// Fyne's resource loader accepts a standalone SFNT, not a TTC collection.
// Repack just the chosen system face in memory. No font is written, embedded,
// installed, downloaded or redistributed. Table offsets and checksums are fixed.
func standaloneFontFace(data []byte, index int) ([]byte, error) {
	bad := errors.New("invalid system font collection")
	if len(data) < 12 || index < 0 {
		return nil, bad
	}
	start := 0
	if string(data[:4]) == "ttcf" {
		count := int(binary.BigEndian.Uint32(data[8:12]))
		if count < 1 || count > 128 || index >= count || 12+count*4 > len(data) {
			return nil, bad
		}
		start = int(binary.BigEndian.Uint32(data[12+index*4 : 16+index*4]))
	} else if index != 0 {
		return nil, bad
	}
	if start < 0 || start > len(data)-12 {
		return nil, bad
	}
	count := int(binary.BigEndian.Uint16(data[start+4 : start+6]))
	if count < 1 || count > 256 || start+12+16*count > len(data) {
		return nil, bad
	}
	result := make([]byte, 12+16*count)
	copy(result, data[start:start+12+16*count])
	head := -1
	for i := 0; i < count; i++ {
		entry := result[12+16*i : 28+16*i]
		offset := uint64(binary.BigEndian.Uint32(entry[8:12]))
		length := uint64(binary.BigEndian.Uint32(entry[12:16]))
		if offset+length > uint64(len(data)) || uint64(len(result))+length > 128<<20 {
			return nil, bad
		}
		dest := len(result)
		result = append(result, data[int(offset):int(offset+length)]...)
		for len(result)%4 != 0 {
			result = append(result, 0)
		}
		entry = result[12+16*i : 28+16*i]
		binary.BigEndian.PutUint32(entry[8:12], uint32(dest))
		if string(entry[:4]) == "head" {
			if length < 12 {
				return nil, bad
			}
			head = dest
			binary.BigEndian.PutUint32(result[head+8:head+12], 0)
		}
		binary.BigEndian.PutUint32(entry[4:8], fontChecksum(result[dest:]))
	}
	if head < 0 {
		return nil, bad
	}
	binary.BigEndian.PutUint32(result[head+8:head+12], 0xb1b0afba-fontChecksum(result))
	return result, nil
}
func fontChecksum(data []byte) uint32 {
	var sum uint32
	for i := 0; i < len(data); i += 4 {
		var word [4]byte
		copy(word[:], data[i:min(i+4, len(data))])
		sum += binary.BigEndian.Uint32(word[:])
	}
	return sum
}

func SystemUIFontSnapshot() map[string]any {
	fontOnce.Do(loadDesktopFonts)
	return map[string]any{"platform": runtime.GOOS, "uiRegularFamily": uiFontFamily, "uiBoldFamily": uiBoldFamily, "systemSource": uiFontSource, "systemCJKPrimary": uiSansRegular != nil, "fontsRedistributed": false, "monospacePolicy": "existing platform monospace and Fyne fallback unchanged"}
}
