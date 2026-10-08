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

#[cfg(test)]
mod tests {
    use super::*;
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
