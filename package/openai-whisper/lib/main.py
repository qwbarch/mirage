from src.model import WhisperModelCT2
import json
import sys
import torch
import traceback
import base64

with open("pylog.txt", 'w') as file:
    try:
        def log(m):
            file.write(f"{m}\n")
            file.flush()
        if __name__ == "__main__":
            stdin = sys.stdin.buffer
            stdout = sys.stdout.buffer

            log("waiting for schema")

            # Store the request schema.
            model_path = stdin.readline().decode("utf-8-sig").strip()
            log(f"model_path: {model_path}")

            def respond(response):
                print(json.dumps(response), flush=True)

            # Initialize Whisper, and notify the process invoker whether CUDA is enabled or not.
            use_cuda = torch.cuda.is_available()
            log(f"cuda available: {use_cuda}")
            model = WhisperModelCT2(
                model_path=model_path,
                device="cuda" if use_cuda else "cpu",
                compute_type="float16" if use_cuda else "float32",
            )
            print(str(use_cuda), flush=True)

            def transcribe(request):
                log(request)
                samples_batch = request["samplesBatch"]
                return model.transcribe_with_vad(
                    samples_batch=[bytes(base64.b64decode(sample)) for sample in samples_batch],
                    lang_codes=[request["language"]] * len(samples_batch),
                    batch_size=32, # Taken from the WhisperS2T example. I'm assuming this is optimal for CTranslate2.
                )

            running = True
            while running:
                try:
                    log("waiting recv")
                    request_unparsed = stdin.readline().decode("utf-8-sig")
                    log(f"request: {request_unparsed}")
                    request = json.loads(request_unparsed)
                    log("recv received")
                    log("finished parsing request")
                    respond({"response": transcribe(request)})
                    log("finished sending response")
                except Exception as e:
                    log(f"exception found.: {traceback.format_exc()}")
                    respond({"exception": traceback.format_exc()})
                    running = False
    except Exception as e:
        log(f"exception found.: {traceback.format_exc()}")