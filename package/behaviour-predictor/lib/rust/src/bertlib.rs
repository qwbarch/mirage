use std::io::{BufRead, BufReader, Write};
use std::os::raw::c_char;
use std::ffi::{CString, CStr};
use std::process::{Command, Stdio};
use lazy_static::lazy_static;

lazy_static! {
    static ref PROCESS: std::sync::Mutex<Option<std::process::Child>> = std::sync::Mutex::new(None);
}

#[no_mangle]
pub extern "C" fn init_bert(exe_path: *const c_char) {
    let exe_path_cstr = unsafe {
        assert!(!exe_path.is_null());
        CStr::from_ptr(exe_path)
    };
    let exe_path_str = exe_path_cstr.to_str().expect("Invalid UTF-8 in exe_path");
    let process = match Command::new(exe_path_str)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .spawn() {
            Err(why) => panic!("couldn't spawn child: {}", why),
            Ok(process) => process,
    };
    let mut guard = PROCESS.lock().unwrap();
    *guard = Some(process);
}

#[no_mangle]
pub extern "C" fn ping(x: i32) -> i32 {
    return 2 * x;
}

#[no_mangle]
pub extern "C" fn pingStr(batch_size: i32, sentences: *const *const c_char) {
    unsafe {
        for i in 0..batch_size {
            let sentence_cstr = CStr::from_ptr(*sentences.offset(i as isize));
            println!("{}", sentence_cstr.to_str().unwrap());
        }
    }
}

const EMBEDDING_LENGTH: i32 = 384;

#[no_mangle]
pub extern "C" fn encode(batch_size: i32, sentences: *const *const c_char, output: *mut f32) {
    let mut guard = PROCESS.lock().unwrap();
    let process = guard.as_mut().expect("Process not initialized");

    let mut child_stdin = process.stdin.take().expect("Failed to open stdin");
    let child_stdout = process.stdout.take().expect("Failed to open stdout");

    let batch_str = batch_size.to_string() + "\0";
    let _ = child_stdin.write_all(batch_str.as_bytes());
    unsafe {
        for i in 0..batch_size {
            let sentence_cstr = CStr::from_ptr(*sentences.offset(i as isize));
            let _ = child_stdin.write_all(sentence_cstr.to_bytes_with_nul());
        }
    }
    child_stdin.flush().expect("Failed to flush child process stdin");

    let mut reader = BufReader::new(child_stdout);
    let mut buf = Vec::new();

    for i in 0..batch_size {
        for j in 0..EMBEDDING_LENGTH {
            let read_bytes = reader.read_until(0, &mut buf).expect("Failed to read a float.");
            let byte_string = CString::new(&buf[0..(read_bytes - 1)]).expect("Something went wrong converting to CString.");
            buf.clear();
            let float_val: f32 = byte_string.to_str().unwrap().parse().expect("Could not parse to a float.");
            unsafe {
                *output.offset((EMBEDDING_LENGTH * i + j) as isize) = float_val;
            }
        }
    }

    // Restore the previous process stdin and stdout
    let child_stdout = reader.into_inner();
    process.stdin = Some(child_stdin);
    process.stdout = Some(child_stdout);
}
