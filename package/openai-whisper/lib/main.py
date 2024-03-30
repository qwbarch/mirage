from src.model import WhisperModelCT2

import torch
import sys
import json
import traceback

def run_request(request):
    body = request["body"]
    global model
    match request["requestType"]:
        case "isCudaAvailable":
            return torch.cuda.is_available()
        case "initModel":
            use_cuda = body["useCuda"]
            model = WhisperModelCT2(
                model_path=body["modelPath"],
                device="cuda" if use_cuda else "cpu",
                compute_type="float16" if use_cuda else "float32",
                cpu_threads=body["cpuThreads"],
                num_workers=body["workers"],
            )
        case "transcribe":
<<<<<<< Updated upstream
            samples_batch = body["samplesBatch"]
            batch_size = len(samples_batch)
            return model.transcribe_with_vad(
                samples_batch=list(map(bytes, samples_batch)),
                lang_codes=[body["language"]] * batch_size,
                batch_size=32, # Taken from the WhisperS2T example. I'm assuming this is optimal for CTranslate2.
=======
            batchSize = len(body["samplesBatch"])
            return transcribe_samples(
                model,
                samples_batch=list(map(bytes, body["samplesBatch"])),
                lang_codes=[body["language"]] * batchSize,
                batch_size=32,
>>>>>>> Stashed changes
            )

def read_null_terminated_utf_string(input_stream):
    buffer = bytearray()
    while True:
        byte = input_stream.read(1)
        if not byte:
            raise EOFError(f"Unexpected end of input stream. byte: {byte}")
        if byte == b"\x00":
            break
        buffer.extend(byte)
    return buffer.decode("utf-8-sig")

if __name__ == "__main__":
    global model
    stdin = sys.stdin.buffer
    stdout = sys.stdout.buffer
    def write_response(response):
        stdout.write(json.dumps(response).encode("utf-8"))
        stdout.write(b"\x00")
        stdout.flush()
    while True:
        try:
            input = json.loads(read_null_terminated_utf_string(stdin))
            response = run_request(input)
            write_response({ "response": response })
        except Exception:
            write_response({ "exception": traceback.format_exc() })
            break