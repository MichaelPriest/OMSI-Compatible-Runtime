//! First real Rust subsystem of the compatible runtime: OMSI texture lookup.
//! The interface is a stable C ABI so WinUI 3 and the existing C#/D3D11
//! runtime can remain operational while additional parsers are migrated.
//! Reference behavior: openOMSI's omsi-texture::find_texture_in_dir (MIT).

use std::ffi::{CStr, c_char};
use std::fs;
use std::path::{Component, Path, PathBuf};

pub const ABI_VERSION: u32 = 1;
const SEPARATOR: char = '\u{1f}';
const EXTENSIONS: &[&str] = &["dds", "png", "tga", "bmp", "jpg", "jpeg", "webp", "gif"];

/// Every path must stay within its asset-category root, including symlinks.
fn normalized(path: &Path) -> PathBuf {
    let mut result = PathBuf::new();
    for part in path.components() {
        match part {
            Component::CurDir => {}
            Component::ParentDir => { result.pop(); }
            other => result.push(other.as_os_str()),
        }
    }
    result
}

fn existing_inside(root: &Path, candidate: PathBuf) -> Option<PathBuf> {
    let candidate = normalized(&candidate);
    // The cheap lexical check prevents even probing an escaped path.
    if !candidate.starts_with(root) || !candidate.is_file() {
        return None;
    }
    let real_root = fs::canonicalize(root).ok()?;
    let real_candidate = fs::canonicalize(&candidate).ok()?;
    real_candidate.starts_with(&real_root).then_some(candidate)
}

fn probe(root: &Path, directory: &Path, name: &Path) -> Option<PathBuf> {
    let original = directory.join(name);
    if let Some(found) = existing_inside(root, original.with_extension("dds")) {
        return Some(found);
    }
    if let Some(found) = existing_inside(root, original.clone()) {
        return Some(found);
    }
    for extension in EXTENSIONS {
        if original.extension().and_then(|e| e.to_str())
            .is_some_and(|e| e.eq_ignore_ascii_case(extension))
        {
            continue;
        }
        if let Some(found) = existing_inside(root, original.with_extension(extension)) {
            return Some(found);
        }
    }
    None
}

/// Search the same OMSI content directories used by the C# renderer.
/// The fallback remains available during migration until real-asset parity
/// has been demonstrated.
pub fn find_texture(root: &Path, texture_name: &str, directories: &[PathBuf]) -> Option<PathBuf> {
    let root = normalized(root);
    if !root.is_absolute() {
        return None;
    }
    let raw = texture_name.trim().trim_matches('"').trim();
    let slash = raw.replace('\\', "/");
    let name = Path::new(&slash);
    if raw.is_empty() || raw.starts_with('\\') || raw.starts_with('/')
        || raw.contains(':') || name.is_absolute()
        || !EXTENSIONS.iter().any(|ext|
            name.extension().and_then(|e| e.to_str())
                .is_some_and(|found| found.eq_ignore_ascii_case(ext)))
    {
        return None;
    }

    // Add-ons sometimes declare "Splines\Pack\Texture\road.bmp" instead
    // of a path relative to the current .sli/.sco. Only known category
    // prefixes are eligible; crossing category roots is never allowed.
    let category = root.file_name()?.to_str()?;
    if ["Splines", "Sceneryobjects", "Vehicles"]
        .iter().any(|c| category.eq_ignore_ascii_case(c))
    {
        if let Some((prefix, rest)) = slash.split_once('/') {
            if prefix.eq_ignore_ascii_case(category) {
                if let Some(path) = probe(&root, &root, Path::new(rest)) {
                    return Some(path);
                }
            }
        }
    }

    for directory in directories {
        if let Some(path) = probe(&root, directory, name) {
            return Some(path);
        }
    }
    // OMSI also checks texture directories of parent object packages.
    for directory in directories {
        let mut current = normalized(directory);
        while current.starts_with(&root) {
            if let Some(path) = probe(&root, &current, name) {
                return Some(path);
            }
            if let Some(path) = probe(&root, &current.join("Texture"), name) {
                return Some(path);
            }
            if !current.pop() {
                break;
            }
        }
    }
    None
}

#[unsafe(no_mangle)]
pub extern "C" fn omsi_core_abi_version() -> u32 {
    ABI_VERSION
}

/// All strings are UTF-8, with directories separated by ASCII unit separator
/// (0x1f, illegal in Windows path segments). Returns bytes written, 0 for a
/// normal miss, -1 for invalid input, -2 when output capacity is insufficient.
/// Never returns an unmanaged allocation; never unwinds into C#.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn omsi_texture_resolve_utf8(
    root: *const c_char,
    texture_name: *const c_char,
    directories: *const c_char,
    output: *mut u8,
    capacity: usize,
) -> i32 {
    std::panic::catch_unwind(|| {
        if root.is_null() || texture_name.is_null() || directories.is_null()
            || output.is_null() || capacity == 0
        {
            return -1;
        }
        // SAFETY: callers must supply valid null-terminated strings and a
        // writable output allocation at least capacity bytes long.
        let strings = unsafe {
            (
                CStr::from_ptr(root).to_str(),
                CStr::from_ptr(texture_name).to_str(),
                CStr::from_ptr(directories).to_str(),
            )
        };
        let (Ok(root), Ok(name), Ok(directories)) = strings else {
            return -1;
        };
        let bases: Vec<PathBuf> = directories
            .split(SEPARATOR)
            .filter(|s| !s.is_empty())
            .map(PathBuf::from)
            .collect();
        let Some(path) = find_texture(Path::new(root), name, &bases) else {
            return 0;
        };
        let path = path.to_string_lossy();
        let bytes = path.as_bytes();
        if bytes.len() >= capacity || bytes.len() > i32::MAX as usize {
            return -2;
        }
        // SAFETY: above guard ensures enough writable bytes, including NUL.
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), output, bytes.len());
            *output.add(bytes.len()) = 0;
        }
        bytes.len() as i32
    })
    .unwrap_or(-1)
}


/// Parse [map] tile declarations from already-decoded UTF-8 global.cfg text.
/// Output is UTF-8 TSV: x<TAB>y<TAB>filename<TAB>source_line<LF>.
/// A filename containing tabs/newlines is rejected so the result is unambiguous.
pub fn parse_global_cfg_tiles(input: &str) -> Vec<(i32, i32, String, usize)> {
    let mut declarations = Vec::new();
    let mut in_map = false;
    let mut header_line = 0usize;
    let mut values: Vec<String> = Vec::new();

    let flush = |values: &mut Vec<String>,
                 header_line: usize,
                 declarations: &mut Vec<(i32, i32, String, usize)>| {
        if values.len() < 3 { values.clear(); return; }
        let x = values[0].trim().parse::<i32>();
        let y = values[1].trim().parse::<i32>();
        let filename = values[2].trim().trim_matches('"').trim().to_owned();
        if let (Ok(x), Ok(y)) = (x, y) {
            if !filename.is_empty() && !filename.contains('\t') && !filename.contains('\r') && !filename.contains('\n') {
                declarations.push((x, y, filename, header_line));
            }
        }
        values.clear();
    };

    for (index, raw) in input.lines().enumerate() {
        let line = raw.trim().trim_start_matches('\u{feff}').trim();
        if line.starts_with('[') && line.ends_with(']') && line.len() >= 3 {
            if in_map {
                flush(&mut values, header_line, &mut declarations);
            }
            in_map = line[1..line.len() - 1].trim().eq_ignore_ascii_case("map");
            header_line = if in_map { index + 1 } else { 0 };
            continue;
        }
        if in_map && !line.is_empty() {
            values.push(line.to_owned());
        }
    }
    if in_map {
        flush(&mut values, header_line, &mut declarations);
    }
    declarations
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn omsi_globalcfg_parse_tiles_utf8(
    input: *const c_char,
    output: *mut u8,
    capacity: usize,
) -> i32 {
    std::panic::catch_unwind(|| {
        if input.is_null() || output.is_null() || capacity == 0 { return -1; }
        // SAFETY: caller supplies a valid NUL-terminated UTF-8 input and writable output.
        let input = match unsafe { CStr::from_ptr(input) }.to_str() {
            Ok(value) => value,
            Err(_) => return -1,
        };
        let mut encoded = String::new();
        for (x, y, filename, source_line) in parse_global_cfg_tiles(input) {
            use std::fmt::Write as _;
            if writeln!(&mut encoded, "{x}\t{y}\t{filename}\t{source_line}").is_err() {
                return -1;
            }
        }
        let bytes = encoded.as_bytes();
        if bytes.len() >= capacity || bytes.len() > i32::MAX as usize { return -2; }
        // SAFETY: capacity guard reserves one byte for the trailing NUL.
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), output, bytes.len());
            *output.add(bytes.len()) = 0;
        }
        bytes.len() as i32
    }).unwrap_or(-1)
}


/// Structural summary of an OMSI binary O3D mesh. This validates section
/// boundaries before renderer integration; it does not decode vertices or
/// apply the version 4/5 scrambling transform.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct O3dSummary {
    pub version: u8,
    pub flags: u8,
    pub vertex_count: u32,
    pub triangle_count: u32,
    pub material_count: u16,
    pub has_matrix: bool,
    pub bone_count: u16,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum O3dError {
    Truncated { offset: usize },
    BadMagic,
    UnsupportedVersion(u8),
    CountOverflow,
    InvalidVertexIndex { index: u32, vertex_count: u32 },
    InvalidMaterialIndex { index: u16, material_count: u16 },
}

struct O3dCursor<'a> {
    bytes: &'a [u8],
    offset: usize,
}

impl<'a> O3dCursor<'a> {
    fn take(&mut self, count: usize) -> Result<&'a [u8], O3dError> {
        let end = self.offset.checked_add(count).ok_or(O3dError::CountOverflow)?;
        if end > self.bytes.len() {
            return Err(O3dError::Truncated { offset: self.offset });
        }
        let value = &self.bytes[self.offset..end];
        self.offset = end;
        Ok(value)
    }

    fn u8(&mut self) -> Result<u8, O3dError> {
        Ok(self.take(1)?[0])
    }

    fn u16(&mut self) -> Result<u16, O3dError> {
        Ok(u16::from_le_bytes(self.take(2)?.try_into().unwrap()))
    }

    fn u32(&mut self) -> Result<u32, O3dError> {
        Ok(u32::from_le_bytes(self.take(4)?.try_into().unwrap()))
    }

    fn count(&mut self, wide: bool) -> Result<u32, O3dError> {
        if wide { self.u32() } else { Ok(self.u16()? as u32) }
    }

    fn f32(&mut self) -> Result<f32, O3dError> {
        Ok(f32::from_bits(self.u32()?))
    }

    fn string8(&mut self) -> Result<String, O3dError> {
        let length = self.u8()? as usize;
        let raw = self.take(length)?;
        Ok(raw.iter().map(|byte| match *byte {
            0x80 => '\u{20ac}', 0x82 => '\u{201a}', 0x83 => '\u{0192}',
            0x84 => '\u{201e}', 0x85 => '\u{2026}', 0x86 => '\u{2020}',
            0x87 => '\u{2021}', 0x88 => '\u{02c6}', 0x89 => '\u{2030}',
            0x8a => '\u{0160}', 0x8b => '\u{2039}', 0x8c => '\u{0152}',
            0x8e => '\u{017d}', 0x91 => '\u{2018}', 0x92 => '\u{2019}',
            0x93 => '\u{201c}', 0x94 => '\u{201d}', 0x95 => '\u{2022}',
            0x96 => '\u{2013}', 0x97 => '\u{2014}', 0x98 => '\u{02dc}',
            0x99 => '\u{2122}', 0x9a => '\u{0161}', 0x9b => '\u{203a}',
            0x9c => '\u{0153}', 0x9e => '\u{017e}', 0x9f => '\u{0178}',
            value => char::from(value),
        }).collect())
    }
}

/// Decoded vertex in OMSI coordinates (X right, Y forward, Z up).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct O3dVertex {
    pub position: [f32; 3],
    pub normal: [f32; 3],
    pub uv: [f32; 2],
}

/// Decoded triangle and its zero-based material index.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct O3dTriangle {
    pub indices: [u32; 3],
    pub material: u16,
}

#[derive(Debug, Clone, PartialEq)]
pub struct O3dMaterial {
    pub diffuse: [f32; 4],
    pub specular: [f32; 3],
    pub emissive: [f32; 3],
    pub specular_power: f32,
    pub texture: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct O3dBoneWeight {
    pub vertex: u32,
    pub weight: f32,
}

#[derive(Debug, Clone, PartialEq)]
pub struct O3dBone {
    pub name: String,
    pub weights: Vec<O3dBoneWeight>,
}

/// Fully decoded mesh data. The matrix is kept in the file's original 16-float order.
#[derive(Debug, Clone, PartialEq)]
pub struct O3dMesh {
    pub version: u8,
    pub flags: u8,
    pub vertices: Vec<O3dVertex>,
    pub triangles: Vec<O3dTriangle>,
    pub materials: Vec<O3dMaterial>,
    pub matrix: Option<[f32; 16]>,
    pub bones: Vec<O3dBone>,
}

fn o3d_fraction_byte(position: [f32; 3]) -> u32 {
    let fraction = |value: f32| (value as f64) - (value as f64).trunc();
    let product = (fraction(position[0]) * fraction(position[1]) * fraction(position[2])).abs() * 600.0;
    (product.trunc() as i64).rem_euclid(256) as u32
}

fn o3d_unscramble(state: u16, vertex: &mut O3dVertex) {
    let s = state;
    if s < 1000 {
        vertex.position.swap(0, 1);
    } else if s < 3000 {
        vertex.position.swap(0, 2);
    } else if s > 7000 {
        vertex.position.swap(1, 2);
    }
    if s & 3 == 0 { vertex.normal[0] = -vertex.normal[0]; }
    if s % 6 == 0 { vertex.normal[1] = -vertex.normal[1]; }
    if s % 7 == 0 { vertex.normal[2] = -vertex.normal[2]; }
    if s < 600 {
        vertex.normal.swap(1, 2);
    } else if s > 4500 {
        // The managed OMSI decoder uses this mutually-exclusive branch;
        // values above 6500 are already covered by the >4500 condition.
        vertex.normal.swap(0, 1);
    }
    if s % 5 == 0 {
        let m = (s % 100) as f32;
        vertex.uv[0] -= m * m / 10000.0;
    }
    if s % 3 == 0 {
        let m = (s % 50) as f32;
        vertex.uv[1] -= m * m / 2500.0;
    }
}

/// Decode vertices, normals, UVs, triangle indices, materials, transform and bones.
/// This is not wired into the renderer yet; callers should continue using the
/// established loader until parity is checked against real installed models.
pub fn decode_o3d(bytes: &[u8]) -> Result<O3dMesh, O3dError> {
    let mut cursor = O3dCursor { bytes, offset: 0 };
    if cursor.take(2)? != [0x84, 0x19] {
        return Err(O3dError::BadMagic);
    }
    let version = cursor.u8()?;
    if !matches!(version, 1 | 3 | 4 | 5 | 7) {
        return Err(O3dError::UnsupportedVersion(version));
    }
    let flags = if version >= 3 { cursor.u8()? } else { 0 };
    let key = if version >= 4 { cursor.u32()? } else { 0 };
    let wide_count = version >= 3;
    let wide_index = version >= 3 && flags & 1 != 0;
    // Match OmsiO3dGeometryReader's protected-vertex sentinel and seed
    // arithmetic exactly. The managed loader treats 0xFFFFFFFF as unprotected.
    // the initial seed wraps to u16 before the alternative offset is applied.
    // 0xFFFFFFFF means unprotected; 0x0000FFFF is a second legacy
    // sentinel handled by OmsiO3dProtectedVertexDecoder as a no-op.
    let scrambled = version >= 4 && key != 0xffff_ffff && key != 0x0000_ffff;
    let alt = flags & 2 != 0;
    let mut state = if scrambled {
        let version_offset = if version > 3 { (version - 4) as u32 } else { 0 };
        let initial = key.wrapping_add(version_offset) as u16;
        ((initial as u32 + if alt { 381 } else { 0 }) % 65_000) as u16
    } else { 0 };
    let mut mesh = O3dMesh {
        version, flags, vertices: Vec::new(), triangles: Vec::new(),
        materials: Vec::new(), matrix: None, bones: Vec::new(),
    };

    while cursor.offset < bytes.len() {
        match cursor.u8()? {
            0x17 => {
                let count = cursor.count(wide_count)? as usize;
                let byte_count = count.checked_mul(32).ok_or(O3dError::CountOverflow)?;
                let end = cursor.offset.checked_add(byte_count).ok_or(O3dError::CountOverflow)?;
                if end > bytes.len() {
                    return Err(O3dError::Truncated { offset: cursor.offset });
                }
                mesh.vertices.reserve(count);
                let n16 = (count as i64).rem_euclid(0xfde8) as u32;
                let mut fb = 0u32;
                for _ in 0..count {
                    let mut vertex = O3dVertex {
                        position: [cursor.f32()?, cursor.f32()?, cursor.f32()?],
                        normal: [cursor.f32()?, cursor.f32()?, cursor.f32()?],
                        uv: [cursor.f32()?, cursor.f32()?],
                    };
                    if scrambled {
                        if key == 0 { state = if alt { 0x130 } else { 0 }; }
                        state = ((state as u32 * n16 + n16 * fb) % 8000) as u16;
                        fb = o3d_fraction_byte(vertex.position);
                        o3d_unscramble(state, &mut vertex);
                    }
                    mesh.vertices.push(vertex);
                }
            }
            0x49 => {
                let count = cursor.count(wide_count)? as usize;
                let index_bytes = if wide_index { 4usize } else { 2usize };
                let triangle_bytes = index_bytes.checked_mul(3)
                    .and_then(|n| n.checked_add(2))
                    .and_then(|n| count.checked_mul(n))
                    .ok_or(O3dError::CountOverflow)?;
                let end = cursor.offset.checked_add(triangle_bytes).ok_or(O3dError::CountOverflow)?;
                if end > bytes.len() {
                    return Err(O3dError::Truncated { offset: cursor.offset });
                }
                for _ in 0..count {
                    let mut indices = [0u32; 3];
                    for index in &mut indices {
                        *index = if wide_index { cursor.u32()? } else { cursor.u16()? as u32 };
                    }
                    let material = cursor.u16()?;
                    mesh.triangles.push(O3dTriangle { indices, material });
                }
            }
            0x26 => {
                let count = cursor.u16()? as usize;
                for _ in 0..count {
                    let diffuse = [cursor.f32()?, cursor.f32()?, cursor.f32()?, cursor.f32()?];
                    let specular = [cursor.f32()?, cursor.f32()?, cursor.f32()?];
                    let emissive = [cursor.f32()?, cursor.f32()?, cursor.f32()?];
                    let specular_power = cursor.f32()?;
                    let texture = cursor.string8()?;
                    mesh.materials.push(O3dMaterial {
                        diffuse, specular, emissive, specular_power, texture,
                    });
                }
            }
            0x79 => {
                let mut matrix = [0.0f32; 16];
                for value in &mut matrix { *value = cursor.f32()?; }
                mesh.matrix = Some(matrix);
            }
            0x54 => {
                let count = cursor.u16()? as usize;
                for _ in 0..count {
                    let name = cursor.string8()?;
                    let weight_count = cursor.u16()? as usize;
                    // Validate the complete weight payload before allocating from
                    // an untrusted file-declared count. Each entry contains an
                    // index (16- or 32-bit) and one f32 weight.
                    let index_bytes = if wide_index { 4usize } else { 2usize };
                    let weight_bytes = index_bytes.checked_add(4)
                        .and_then(|size| size.checked_mul(weight_count))
                        .ok_or(O3dError::CountOverflow)?;
                    let end = cursor.offset.checked_add(weight_bytes)
                        .ok_or(O3dError::CountOverflow)?;
                    if end > bytes.len() {
                        return Err(O3dError::Truncated { offset: cursor.offset });
                    }
                    let mut weights = Vec::with_capacity(weight_count);
                    for _ in 0..weight_count {
                        let vertex = if wide_index { cursor.u32()? } else { cursor.u16()? as u32 };
                        let weight = cursor.f32()?;
                        weights.push(O3dBoneWeight { vertex, weight });
                    }
                    mesh.bones.push(O3dBone { name, weights });
                }
            }
            // Keep the original loader's permissive unknown-tag behavior.
            _ => {}
        }
    }

    for triangle in &mesh.triangles {
        for index in triangle.indices {
            if index as usize >= mesh.vertices.len() {
                return Err(O3dError::InvalidVertexIndex {
                    index, vertex_count: mesh.vertices.len().min(u32::MAX as usize) as u32,
                });
            }
        }
        if !mesh.materials.is_empty() && triangle.material as usize >= mesh.materials.len() {
            return Err(O3dError::InvalidMaterialIndex {
                index: triangle.material,
                material_count: mesh.materials.len().min(u16::MAX as usize) as u16,
            });
        }
    }
    for bone in &mesh.bones {
        for weight in &bone.weights {
            if weight.vertex as usize >= mesh.vertices.len() {
                return Err(O3dError::InvalidVertexIndex {
                    index: weight.vertex, vertex_count: mesh.vertices.len().min(u32::MAX as usize) as u32,
                });
            }
        }
    }
    Ok(mesh)
}

/// Stable C representation used by the future C# mesh-loader bridge.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct O3dSummaryC {
    pub version: u8,
    pub flags: u8,
    pub has_matrix: u8,
    pub reserved: u8,
    pub vertex_count: u32,
    pub triangle_count: u32,
    pub material_count: u16,
    pub bone_count: u16,
}

/// Inspect a mesh buffer through the native ABI.
/// Returns 0 on success, -1 for invalid pointers/length, -2 for malformed
/// or unsupported O3D data, and -3 if an unexpected panic is caught.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn omsi_o3d_inspect(
    input: *const u8,
    length: usize,
    output: *mut O3dSummaryC,
) -> i32 {
    std::panic::catch_unwind(|| {
        if input.is_null() || output.is_null() || length == 0 || length > isize::MAX as usize {
            return -1;
        }
        // SAFETY: caller promises input references length readable bytes
        // and output points to a writable O3dSummaryC.
        let bytes = unsafe { std::slice::from_raw_parts(input, length) };
        let summary = match inspect_o3d(bytes) {
            Ok(value) => value,
            Err(_) => return -2,
        };
        unsafe {
            output.write(O3dSummaryC {
                version: summary.version,
                flags: summary.flags,
                has_matrix: u8::from(summary.has_matrix),
                reserved: 0,
                vertex_count: summary.vertex_count,
                triangle_count: summary.triangle_count,
                material_count: summary.material_count,
                bone_count: summary.bone_count,
            });
        }
        0
    })
    .unwrap_or(-3)
}

/// Validate the binary structure of one OMSI O3D mesh and report its declared
/// geometry counts. Unknown section bytes are skipped one at a time, matching
/// the original loader's permissive tag dispatch.
pub fn inspect_o3d(bytes: &[u8]) -> Result<O3dSummary, O3dError> {
    let mut cursor = O3dCursor { bytes, offset: 0 };
    if cursor.take(2)? != [0x84, 0x19] {
        return Err(O3dError::BadMagic);
    }
    let version = cursor.u8()?;
    if !matches!(version, 1 | 3 | 4 | 5 | 7) {
        return Err(O3dError::UnsupportedVersion(version));
    }
    let flags = if version >= 3 { cursor.u8()? } else { 0 };
    let _key = if version >= 4 { cursor.u32()? } else { 0 };
    let wide_count = version >= 3;
    let wide_index = version >= 3 && flags & 1 != 0;
    let mut summary = O3dSummary {
        version, flags, vertex_count: 0, triangle_count: 0,
        material_count: 0, has_matrix: false, bone_count: 0,
    };
    let mut largest_material_index: Option<u16> = None;

    while cursor.offset < bytes.len() {
        let tag = cursor.u8()?;
        match tag {
            0x17 => {
                let count = cursor.count(wide_count)?;
                let byte_count = (count as usize).checked_mul(32).ok_or(O3dError::CountOverflow)?;
                cursor.take(byte_count)?;
                summary.vertex_count = count;
            }
            0x49 => {
                let count = cursor.count(wide_count)?;
                for _ in 0..count {
                    for _ in 0..3 {
                        let index = if wide_index { cursor.u32()? } else { cursor.u16()? as u32 };
                        if index >= summary.vertex_count {
                            return Err(O3dError::InvalidVertexIndex {
                                index,
                                vertex_count: summary.vertex_count,
                            });
                        }
                    }
                    let material_index = cursor.u16()?;
                    largest_material_index = Some(
                        largest_material_index.map_or(material_index, |current| current.max(material_index))
                    );
                }
                summary.triangle_count = count;
            }
            0x26 => {
                let count = cursor.u16()?;
                for _ in 0..count {
                    cursor.take(44)?;
                    cursor.string8()?;
                }
                summary.material_count = count;
            }
            0x79 => {
                cursor.take(64)?;
                summary.has_matrix = true;
            }
            0x54 => {
                let count = cursor.u16()?;
                for _ in 0..count {
                    cursor.string8()?;
                    let weights = cursor.u16()?;
                    for _ in 0..weights {
                        let index = if wide_index { cursor.u32()? } else { cursor.u16()? as u32 };
                        if index >= summary.vertex_count {
                            return Err(O3dError::InvalidVertexIndex {
                                index,
                                vertex_count: summary.vertex_count,
                            });
                        }
                        cursor.take(4)?; // weight
                    }
                }
                summary.bone_count = count;
            }
            _ => {
                // OMSI's loader ignores unrecognized bytes and advances by one.
            }
        }
    }
    if let Some(index) = largest_material_index {
        if index >= summary.material_count {
            return Err(O3dError::InvalidMaterialIndex {
                index,
                material_count: summary.material_count,
            });
        }
    }
    Ok(summary)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn push_u32(bytes: &mut Vec<u8>, value: u32) {
        bytes.extend_from_slice(&value.to_le_bytes());
    }

    fn minimal_o3d(long_indices: bool) -> Vec<u8> {
        let mut bytes = vec![0x84, 0x19, 3, if long_indices { 1 } else { 0 }];
        bytes.push(0x17); // one vertex
        push_u32(&mut bytes, 1);
        bytes.extend_from_slice(&[0; 32]);
        bytes.push(0x49); // one triangle
        push_u32(&mut bytes, 1);
        if long_indices {
            push_u32(&mut bytes, 0);
            push_u32(&mut bytes, 0);
            push_u32(&mut bytes, 0);
        } else {
            bytes.extend_from_slice(&[0; 6]);
        }
        bytes.extend_from_slice(&[0; 2]); // material index
        bytes.push(0x26); // one material
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.extend_from_slice(&[0; 44]);
        bytes.push(3);
        bytes.extend_from_slice(b"mat");
        bytes.push(0x79); // matrix
        bytes.extend_from_slice(&[0; 64]);
        bytes.push(0x54); // one bone with one weight
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.push(1);
        bytes.push(b'b');
        bytes.extend_from_slice(&1u16.to_le_bytes());
        if long_indices {
            push_u32(&mut bytes, 0);
        } else {
            bytes.extend_from_slice(&0u16.to_le_bytes());
        }
        bytes.extend_from_slice(&1.0f32.to_le_bytes());
        bytes
    }

    fn minimal_o3d_v1() -> Vec<u8> {
        let mut bytes = vec![0x84, 0x19, 1];
        bytes.push(0x17); // v1 uses 16-bit section counts
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.extend_from_slice(&[0; 32]);
        bytes.push(0x49);
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.extend_from_slice(&[0; 8]); // three u16 indices + material
        bytes.push(0x26);
        bytes.extend_from_slice(&1u16.to_le_bytes());
        bytes.extend_from_slice(&[0; 44]);
        bytes.push(3);
        bytes.extend_from_slice(b"mat");
        bytes.push(0x79);
        bytes.extend_from_slice(&[0; 64]);
        bytes
    }

    #[test]
    fn o3d_decoder_supports_legacy_version_one_narrow_counts() {
        let mesh = decode_o3d(&minimal_o3d_v1()).unwrap();
        assert_eq!(mesh.version, 1);
        assert_eq!(mesh.vertices.len(), 1);
        assert_eq!(mesh.triangles, vec![O3dTriangle { indices: [0, 0, 0], material: 0 }]);
        assert_eq!(mesh.materials[0].texture, "mat");
        assert_eq!(mesh.matrix, Some([0.0; 16]));
    }

    #[test]
    fn o3d_decoder_supports_versions_five_and_seven_with_legacy_sentinels() {
        // Versions 5 and 7 use the version >= 3 section-count format. Exercise
        // both narrow and wide indices without applying protected-vertex scrambling.
        for (version, wide_indices) in [(5u8, false), (7u8, true)] {
            let original = minimal_o3d(wide_indices);
            let mut bytes = vec![0x84, 0x19, version, if wide_indices { 1 } else { 0 }];
            bytes.extend_from_slice(&0xffff_ffffu32.to_le_bytes());
            bytes.extend_from_slice(&original[4..]);

            let mesh = decode_o3d(&bytes).unwrap();
            assert_eq!(mesh.version, version);
            assert_eq!(mesh.vertices.len(), 1);
            assert_eq!(mesh.triangles[0].indices, [0, 0, 0]);
            assert_eq!(mesh.triangles[0].material, 0);
            assert_eq!(mesh.materials[0].texture, "mat");
            assert_eq!(mesh.bones[0].weights[0].vertex, 0);
        }
    }

    #[test]
    fn o3d_decoder_rejects_bad_magic_and_unsupported_version() {
        let mut bad_magic = minimal_o3d(false);
        bad_magic[0] = 0;
        assert!(matches!(decode_o3d(&bad_magic), Err(O3dError::BadMagic)));

        let mut unsupported = minimal_o3d(false);
        unsupported[2] = 2;
        assert!(matches!(
            decode_o3d(&unsupported),
            Err(O3dError::UnsupportedVersion(2))
        ));
    }

    #[test]
    fn o3d_decoder_rejects_impossible_vertex_count_before_allocation() {
        let bytes = [0x84, 0x19, 3, 0, 0x17, 0xff, 0xff, 0xff, 0xff];
        assert!(matches!(decode_o3d(&bytes), Err(O3dError::Truncated { .. })));
    }

    #[test]
    fn o3d_decoder_returns_geometry_material_and_transform() {
        let bytes = minimal_o3d(false);
        let mesh = decode_o3d(&bytes).unwrap();
        assert_eq!(mesh.version, 3);
        assert_eq!(mesh.vertices.len(), 1);
        assert_eq!(mesh.vertices[0].position, [0.0, 0.0, 0.0]);
        assert_eq!(mesh.vertices[0].normal, [0.0, 0.0, 0.0]);
        assert_eq!(mesh.vertices[0].uv, [0.0, 0.0]);
        assert_eq!(mesh.triangles, vec![O3dTriangle { indices: [0, 0, 0], material: 0 }]);
        assert_eq!(mesh.materials.len(), 1);
        assert_eq!(mesh.materials[0].texture, "mat");
        assert_eq!(mesh.matrix.unwrap(), [0.0; 16]);
        assert_eq!(mesh.bones.len(), 1);
        assert_eq!(mesh.bones[0].weights[0].weight, 1.0);
    }

    #[test]
    fn o3d_decoder_rejects_truncated_triangle_section_before_iteration() {
        let mut bytes = minimal_o3d(false);
        let triangle_start = 4 + 1 + 4 + 32;
        bytes.truncate(triangle_start + 1 + 4 + 2);
        assert!(matches!(
            decode_o3d(&bytes),
            Err(O3dError::Truncated { .. })
        ));
    }

    #[test]
    fn o3d_decoder_rejects_truncated_bone_weights_before_allocation() {
        let mut bytes = minimal_o3d(false);
        // Remove the valid bone's weight payload and declare the maximum
        // 16-bit weight count. Decoder must return a bounds error before Vec allocation.
        bytes.truncate(bytes.len() - 6);
        let count_offset = bytes.len() - 2;
        bytes[count_offset..count_offset + 2].copy_from_slice(&u16::MAX.to_le_bytes());
        assert!(matches!(decode_o3d(&bytes), Err(O3dError::Truncated { .. })));
    }

    #[test]
    fn o3d_decoder_rejects_bad_geometry_references() {
        let mut bytes = minimal_o3d(false);
        let triangle_start = 4 + 1 + 4 + 32 + 1 + 4;
        bytes[triangle_start..triangle_start + 2].copy_from_slice(&1u16.to_le_bytes());
        assert!(matches!(
            decode_o3d(&bytes),
            Err(O3dError::InvalidVertexIndex { index: 1, vertex_count: 1 })
        ));
    }

    #[test]
    fn o3d_decoder_rejects_triangle_material_outside_material_table() {
        let mut bytes = minimal_o3d(false);
        let triangle_start = 4 + 1 + 4 + 32 + 1 + 4;
        bytes[triangle_start + 6..triangle_start + 8]
            .copy_from_slice(&1u16.to_le_bytes());
        assert!(matches!(
            decode_o3d(&bytes),
            Err(O3dError::InvalidMaterialIndex { index: 1, material_count: 1 })
        ));
    }

    #[test]
    fn o3d_decoder_rejects_bone_weight_outside_vertex_buffer() {
        let mut bytes = minimal_o3d(false);
        // Final section is one bone with one (u16 index, f32 weight) entry.
        let weight_index = bytes.len() - 6;
        bytes[weight_index..weight_index + 2].copy_from_slice(&1u16.to_le_bytes());
        assert!(matches!(
            decode_o3d(&bytes),
            Err(O3dError::InvalidVertexIndex { index: 1, vertex_count: 1 })
        ));
    }

    #[test]
    fn o3d_decoder_accepts_version_four_with_unscrambled_key() {
        let original = minimal_o3d(false);
        let mut bytes = vec![0x84, 0x19, 4, 0];
        bytes.extend_from_slice(&0xffff_ffffu32.to_le_bytes());
        bytes.extend_from_slice(&original[4..]);
        let mesh = decode_o3d(&bytes).unwrap();
        assert_eq!(mesh.version, 4);
        assert_eq!(mesh.vertices.len(), 1);
        assert_eq!(mesh.triangles.len(), 1);
        assert_eq!(mesh.materials[0].texture, "mat");
    }

    #[test]
    fn o3d_decoder_treats_legacy_protection_sentinel_as_unprotected() {
        let original = minimal_o3d(false);
        let mut bytes = vec![0x84, 0x19, 4, 0];
        bytes.extend_from_slice(&0x0000_ffffu32.to_le_bytes());
        bytes.extend_from_slice(&original[4..]);
        // Distinct coordinates make an accidental axis swap observable.
        let vertex_start = 8 + 1 + 4;
        bytes[vertex_start..vertex_start + 4].copy_from_slice(&1.0f32.to_le_bytes());
        bytes[vertex_start + 4..vertex_start + 8].copy_from_slice(&2.0f32.to_le_bytes());
        bytes[vertex_start + 8..vertex_start + 12].copy_from_slice(&3.0f32.to_le_bytes());

        let mesh = decode_o3d(&bytes).unwrap();
        assert_eq!(mesh.vertices[0].position, [1.0, 2.0, 3.0]);
    }

    #[test]
    fn o3d_decoder_maps_windows_1252_material_text() {
        let mut bytes = minimal_o3d(false);
        let material_start = 4 + 1 + 4 + 32 + 1 + 4 + 8;
        // Tag + count + 44 bytes of material properties + string length.
        bytes[material_start + 1 + 2 + 44 + 1] = 0x80;
        let mesh = decode_o3d(&bytes).unwrap();
        assert_eq!(mesh.materials[0].texture, "€at");
    }

    #[test]
    fn o3d_inspector_c_abi_returns_a_fixed_summary() {
        let bytes = minimal_o3d(false);
        let mut output = O3dSummaryC::default();
        let result = unsafe { omsi_o3d_inspect(bytes.as_ptr(), bytes.len(), &mut output) };
        assert_eq!(result, 0);
        assert_eq!(output.version, 3);
        assert_eq!(output.vertex_count, 1);
        assert_eq!(output.triangle_count, 1);
        assert_eq!(output.material_count, 1);
        assert_eq!(output.bone_count, 1);
        assert_eq!(output.has_matrix, 1);
    }

    #[test]
    fn o3d_inspector_reads_standard_sections_and_geometry_counts() {
        let summary = inspect_o3d(&minimal_o3d(false)).unwrap();
        assert_eq!(summary, O3dSummary {
            version: 3, flags: 0, vertex_count: 1, triangle_count: 1,
            material_count: 1, has_matrix: true, bone_count: 1,
        });
    }

    #[test]
    fn o3d_inspector_handles_32_bit_indices() {
        let summary = inspect_o3d(&minimal_o3d(true)).unwrap();
        assert_eq!(summary.flags & 1, 1);
        assert_eq!(summary.triangle_count, 1);
        assert_eq!(summary.bone_count, 1);
    }

    #[test]
    fn o3d_inspector_rejects_bad_magic_unsupported_version_and_truncation() {
        assert_eq!(inspect_o3d(&[0, 0, 3, 0]), Err(O3dError::BadMagic));
        assert_eq!(inspect_o3d(&[0x84, 0x19, 2]), Err(O3dError::UnsupportedVersion(2)));
        assert!(matches!(inspect_o3d(&[0x84, 0x19, 3, 0, 0x17, 1, 0, 0]), Err(O3dError::Truncated { .. })));
    }

    #[test]
    fn o3d_inspector_rejects_triangle_indices_outside_vertex_buffer() {
        let mut bytes = minimal_o3d(false);
        // In the minimal fixture, the first triangle index follows the 4-byte
        // vertex section header and 32-byte vertex record.
        let triangle_start = 4 + 1 + 4 + 32 + 1 + 4;
        bytes[triangle_start..triangle_start + 2].copy_from_slice(&1u16.to_le_bytes());
        assert!(matches!(
            inspect_o3d(&bytes),
            Err(O3dError::InvalidVertexIndex { index: 1, vertex_count: 1 })
        ));
    }

    #[test]
    fn o3d_inspector_rejects_triangle_material_outside_material_table() {
        let mut bytes = minimal_o3d(false);
        let triangle_start = 4 + 1 + 4 + 32 + 1 + 4;
        bytes[triangle_start + 6..triangle_start + 8].copy_from_slice(&1u16.to_le_bytes());
        assert!(matches!(
            inspect_o3d(&bytes),
            Err(O3dError::InvalidMaterialIndex { index: 1, material_count: 1 })
        ));
    }


    use std::sync::atomic::{AtomicUsize, Ordering};
    static NEXT: AtomicUsize = AtomicUsize::new(0);

    struct Fixture(PathBuf);
    impl Fixture {
        fn new() -> Self {
            let dir = std::env::temp_dir().join(format!(
                "omsi-rust-texture-{}-{}",
                std::process::id(), NEXT.fetch_add(1, Ordering::Relaxed)
            ));
            fs::create_dir_all(&dir).unwrap();
            Self(dir)
        }
        fn put(&self, name: &str) -> PathBuf {
            let full = self.0.join(name);
            fs::create_dir_all(full.parent().unwrap()).unwrap();
            fs::write(&full, b"texture").unwrap();
            full
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }


    #[test]
    fn parses_global_cfg_map_sections_case_insensitively_and_keeps_source_line() {
        let input = "\u{feff}[header]\r\n1\r\n[MaP]\r\n-2\r\n7\r\nTile -2 7.map\r\n[other]\r\nignored\r\n[map]\r\n0\r\n0\r\nsecond.map\r\n";
        assert_eq!(parse_global_cfg_tiles(input), vec![
            (-2, 7, "Tile -2 7.map".to_owned(), 3),
            (0, 0, "second.map".to_owned(), 9),
        ]);
    }

    #[test]
    fn skips_malformed_or_incomplete_global_cfg_map_declarations() {
        let input = "[map]\nnot-x\n2\nbad.map\n[map]\n1\n2\n\n[other]\n3\n4\nwrong.map\n";
        assert!(parse_global_cfg_tiles(input).is_empty());
    }

    #[test]
    fn global_cfg_abi_reports_capacity_and_emits_tsv() {
        use std::ffi::CString;
        let input = CString::new("[map]\n1\n-3\nTile.map\n").unwrap();
        let mut output = [0_u8; 128];
        let length = unsafe {
            omsi_globalcfg_parse_tiles_utf8(input.as_ptr(), output.as_mut_ptr(), output.len())
        };
        assert_eq!(std::str::from_utf8(&output[..length as usize]).unwrap(), "1\t-3\tTile.map\t1\n");
        let mut tiny = [0_u8; 1];
        assert_eq!(unsafe {
            omsi_globalcfg_parse_tiles_utf8(input.as_ptr(), tiny.as_mut_ptr(), tiny.len())
        }, -2);
    }

    #[test]
    fn local_dds_wins_over_exact_raster_but_not_other_pack() {
        let f = Fixture::new();
        let root = f.0.join("Vehicles");
        let local = root.join("Bus/Texture");
        let other = root.join("Other/Texture");
        f.put("Vehicles/Bus/Texture/panel.bmp");
        let expected = f.put("Vehicles/Bus/Texture/panel.dds");
        f.put("Vehicles/Other/Texture/panel.dds");
        assert_eq!(find_texture(&root, "panel.bmp", &[local, other]), Some(expected));
    }

    #[test]
    fn accepts_content_relative_spline_paths_and_extension_fallback() {
        let f = Fixture::new();
        let root = f.0.join("Splines");
        let expected = f.put("Splines/Pack/Texture/road.dds");
        let referring = root.join("Another/Texture");
        fs::create_dir_all(&referring).unwrap();
        assert_eq!(
            find_texture(&root, r"Splines\Pack\Texture\road.bmp", &[referring]),
            Some(expected)
        );
    }

    #[test]
    fn rejects_escaped_categories_and_absolute_names() {
        let f = Fixture::new();
        let root = f.0.join("Splines");
        let referring = root.join("Pack");
        fs::create_dir_all(&referring).unwrap();
        f.put("Vehicles/Bus/Texture/private.dds");
        for bad in [
            r"Splines\..\..\Vehicles\Bus\Texture\private.dds",
            r"..\..\Vehicles\Bus\Texture\private.dds",
            r"C:\Windows\private.dds",
            "/tmp/private.dds",
        ] {
            assert_eq!(find_texture(&root, bad, &[referring.clone()]), None);
        }
    }

    #[test]
    fn native_abi_writes_utf8_and_checks_capacity() {
        use std::ffi::CString;
        let f = Fixture::new();
        let root = f.0.join("Splines");
        let base = root.join("Pack/Texture");
        let found = f.put("Splines/Pack/Texture/straße.dds");
        let args = (
            CString::new(root.to_str().unwrap()).unwrap(),
            CString::new("straße.bmp").unwrap(),
            CString::new(base.to_str().unwrap()).unwrap(),
        );
        let mut buffer = [0_u8; 4096];
        // SAFETY: NUL-terminated input pointers and a valid writable array.
        let length = unsafe {
            omsi_texture_resolve_utf8(
                args.0.as_ptr(), args.1.as_ptr(), args.2.as_ptr(),
                buffer.as_mut_ptr(), buffer.len(),
            )
        };
        assert!(length > 0);
        // Windows PathBuf normalizes separators when joining, while a
        // literal fixture path may contain forward slashes. Compare paths
        // after component normalization, not byte-for-byte spellings.
        assert_eq!(
            PathBuf::from(std::str::from_utf8(&buffer[..length as usize]).unwrap()),
            normalized(&found)
        );
        assert_eq!(omsi_core_abi_version(), 1);
        let mut tiny = [0_u8; 1];
        let error = unsafe {
            omsi_texture_resolve_utf8(
                args.0.as_ptr(), args.1.as_ptr(), args.2.as_ptr(),
                tiny.as_mut_ptr(), tiny.len(),
            )
        };
        assert_eq!(error, -2);
    }
}
