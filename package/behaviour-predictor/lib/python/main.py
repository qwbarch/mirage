from sentence_transformers import SentenceTransformer
import sys

model = SentenceTransformer('paraphrase-multilingual-MiniLM-L12-v2')
EMB = 384

def dbg(x):
    print(x, file=sys.stderr)

def write_null_separated(lst : list[float], output_stream):
    t = ""
    for i in range(len(lst)):
        t += "{:.8}".format(lst[i])
        t += '\x00'

    output_stream.write(t.encode('utf-8'))

def read_null_terminated_utf_string(input_stream):
    buffer = bytearray()
    while True:
        byte = input_stream.read(1)
        if not byte:
            raise EOFError("Unexpected end of input stream.")
        if byte == b'\x00':
            break
        buffer.extend(byte)
    return buffer.decode('utf-8')

def iter(input_stream, output_stream):
    batch_size = int(read_null_terminated_utf_string(input_stream))
    sentences = []
    for i in range(batch_size):
        sentences.append(read_null_terminated_utf_string(input_stream))

    embeddings = model.encode(sentences, normalize_embeddings=True)
    for embedding in embeddings:
        write_null_separated(embedding.tolist(), output_stream)

if __name__ == '__main__':
    input_stream = sys.stdin.buffer
    output_stream = sys.stdout.buffer

    while True:
        try:
            iter(input_stream, output_stream)
            output_stream.flush()
        except EOFError as e:
            break
        except Exception as e:
            break