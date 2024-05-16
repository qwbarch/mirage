from src.model import WhisperModelCT2
import json
import sys
import torch
import traceback
import zmq

if __name__ == "__main__":
    stdin = sys.stdin.buffer
    stdout = sys.stdout.buffer

    # Initialize ZeroMQ.
    context = zmq.Context()
    socket = context.socket(zmq.REP)
    socket.bind("tcp://localhost:50292")

    def respond(response):
        socket.send(json.dumps(response).encode("utf-8"))

    # Initialize Whisper, and notify the process invoker whether CUDA is enabled or not.
    use_cuda = torch.cuda.is_available()
    model = WhisperModelCT2(
        model_path="model/whisper-base",
        device="cuda" if use_cuda else "cpu",
        compute_type="float16" if use_cuda else "float32",
    )
    print(str(use_cuda), flush=True)

    def transcribe(request):
        samples_batch = request["samplesBatch"]
        return model.transcribe_with_vad(
            samples_batch=list(map(bytes, samples_batch)),
            lang_codes=[request["language"]] * len(samples_batch),
            batch_size=32, # Taken from the WhisperS2T example. I'm assuming this is optimal for CTranslate2.
        )

    while True:
        try:
            request = socket.recv().decode("utf-8")
            response = transcribe(json.loads(request))
            respond(response)
        except Exception:
            respond({ "exception": traceback.format_exc() })
            break