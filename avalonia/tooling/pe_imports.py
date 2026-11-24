"""Read PE regular and delay-load DLL names for x86/x64/ARM64 without executing.
Uses the PE/COFF data-directory layout; rejects malformed/out-of-file mappings.
"""
import struct

def read_imports(path):
    data = path.read_bytes()
    def unpack(fmt, offset):
        if offset < 0 or offset + struct.calcsize(fmt) > len(data):
            raise ValueError("PE structure outside file")
        return struct.unpack_from(fmt, data, offset)
    pe = unpack("<I", 0x3c)[0]
    if data[:2] != b"MZ" or data[pe:pe+4] != b"PE\0\0":
        raise ValueError("Not PE")
    machine, sections = unpack("<HH", pe+4)
    optional_size = unpack("<H", pe+20)[0]
    optional = pe+24
    magic = unpack("<H", optional)[0]
    if magic == 0x20b:
        directories, count_offset = optional+112, optional+108
        image_base = unpack("<Q", optional+24)[0]
    elif magic == 0x10b:
        directories, count_offset = optional+96, optional+92
        image_base = unpack("<I", optional+28)[0]
    else:
        raise ValueError("Unsupported optional header")
    count = unpack("<I", count_offset)[0]
    if sections > 96 or count > 64 or directories + count*8 > optional + optional_size:
        raise ValueError("PE header bounds invalid")
    headers_size = unpack("<I", optional+60)[0]
    mappings = []
    for i in range(sections):
        start = optional+optional_size+i*40
        virtual_size, virtual_address, raw_size, raw = unpack("<IIII", start+8)
        mappings.append((virtual_address, raw_size, raw))
    def offset(rva):
        if 0 <= rva < min(headers_size, len(data)):
            return rva
        for va, size, raw in mappings:
            if va <= rva < va+size and raw+rva-va < len(data):
                return raw+rva-va
        raise ValueError("Unmapped PE RVA")
    def name(rva):
        start = offset(rva)
        end = data.find(b"\0", start, min(start+1025, len(data)))
        if end < 0 or end == start:
            raise ValueError("Invalid DLL name")
        value = data[start:end].decode("ascii")
        if any(ord(c) < 32 for c in value) or "/" in value or "\\" in value:
            raise ValueError("Invalid DLL name")
        return value
    result = {}
    for key, index, size in [("regular", 1, 20), ("delay", 13, 32)]:
        found = []
        if count > index:
            rva, length = unpack("<II", directories+index*8)
            if rva:
                if length < size or length > 4096*size:
                    raise ValueError("Import table bounds invalid")
                for i in range(length//size):
                    fields = unpack("<"+"I"*(size//4), offset(rva+i*size))
                    if not any(fields):
                        break
                    if key == "regular":
                        name_rva = fields[3]
                    else:
                        attrs, name_rva = fields[:2]
                        if attrs & ~1:
                            raise ValueError("Unknown delay descriptor")
                        if not attrs & 1:
                            name_rva -= image_base
                    found.append(name(name_rva))
                else:
                    raise ValueError("Unterminated import table")
        result[key] = sorted(set(found), key=str.lower)
    result["machine"] = hex(machine)
    return result
