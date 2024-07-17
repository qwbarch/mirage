mod bertlib;

use std::ffi::CString;
use std::os::raw::{c_char, c_void};
use std::ptr::null_mut;
use std::path::PathBuf;

const DLL_NAME: &str = "bertlib.dll";

#[cfg(windows)]
fn find_dll_path() -> Option<PathBuf> {
    use std::env;
    use std::path::Path;
    env::current_exe()
        .ok()
        .and_then(|p| {
            p.parent()
                .map(|p| p.join(DLL_NAME))
                .filter(|p| p.exists())
        })
}

fn main() {
    if let Some(dll_path) = find_dll_path() {
        unsafe {
            let dll_handle = winapi::um::libloaderapi::LoadLibraryA(
                CString::new(dll_path.to_str().unwrap())
                    .expect("Failed to convert DLL path to CString")
                    .as_ptr(),
            );
            if dll_handle.is_null() {
                panic!("Failed to load DLL");
            }

            let encode_func_ptr = winapi::um::libloaderapi::GetProcAddress(
                dll_handle,
                CString::new("encode").unwrap().as_ptr(),
            );

            if encode_func_ptr.is_null() {
                panic!("Failed to find function");
            }

            let encode_func: extern "C" fn(i32, *const *const c_char, *mut f32) = std::mem::transmute(encode_func_ptr);
            let strings = vec!["Rust\0".as_ptr(), "apple\0".as_ptr()];
            let ptrs: Vec<*const c_char> = strings.iter().copied().map(|ptr| ptr as *const c_char).collect();

            let mut emb = Vec::new();
            for _ in 0..strings.len() {
                let mut row = vec![0.0; 384];
                emb.push(row);
            }

            let mut emb_ptr: Vec<f32> = vec!(0.0; strings.len() * 384);
            
            encode_func(strings.len() as i32, ptrs.as_ptr(), emb_ptr.as_mut_ptr());

            unsafe {
                // Print the values of emb after the function call
                for i in 0..strings.len() {
                    println!("{} {}", i, emb_ptr[384*i]);
                }
            }

            encode_func(strings.len() as i32, ptrs.as_ptr(), emb_ptr.as_mut_ptr());

            unsafe {
                // Print the values of emb after the function call
                for i in 0..strings.len() {
                    println!("{} {}", i, emb_ptr[384*i]);
                }
            }

            winapi::um::libloaderapi::FreeLibrary(dll_handle);
            println!("Done.");
        }
    } else {
        println!("Failed to find DLL");
    }
}
