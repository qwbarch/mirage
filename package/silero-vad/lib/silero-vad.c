#include "onnxruntime_c_api.h"
#include <windows.h>

#define WINDOW_SIZE 1024
#define HC_LENGTH 2 * 1 * 64

const int64_t input_shape[] = {1, WINDOW_SIZE};
const int64_t sr_shape[] = {1};
const int64_t hc_shape[] = {2, 1, 64};

const char* input_names[] = {"input", "sr", "h", "c"};
const char* output_names[] = {"output", "hn", "cn"};

struct SileroVAD
{
    const OrtApi* api;
    OrtEnv* env;
    OrtSessionOptions* session_options;
    OrtSession* session;
    OrtMemoryInfo* memory_info;
    float* h;
    float* c;
};

struct SileroInitParams
{
    const wchar_t* model_path;
    OrtLoggingLevel log_level;
    int intra_threads;
    int inter_threads;
};

__declspec(dllexport) struct SileroVAD* init_silero(struct SileroInitParams init_params)
{
    printf("model_path: %ls\n", init_params.model_path);
    struct SileroVAD* vad = malloc(sizeof(struct SileroVAD));
    OrtApiBase* ort_api_base = (OrtApiBase*) GetProcAddress(LoadLibraryA("onnxruntime.dll"), "OrtGetApiBase")();
    vad->api = ort_api_base->GetApi(ORT_API_VERSION);
    vad->api->CreateEnv(init_params.log_level, "Mirage", &vad->env);
    vad->api->CreateSessionOptions(&vad->session_options);
    vad->api->SetIntraOpNumThreads(vad->session_options, init_params.intra_threads);
    vad->api->SetInterOpNumThreads(vad->session_options, init_params.inter_threads);
    vad->api->SetSessionGraphOptimizationLevel(vad->session_options, ORT_ENABLE_ALL);
    vad->api->CreateSession(vad->env, init_params.model_path, vad->session_options, &vad->session);
    vad->api->CreateCpuMemoryInfo(OrtArenaAllocator, OrtMemTypeCPU, &vad->memory_info);
    const int hc_bytes = HC_LENGTH * sizeof(float);
    vad->h = (float*) malloc(hc_bytes);
    vad->c = (float*) malloc(hc_bytes);
    memset(vad->h, 0.0f, hc_bytes);
    memset(vad->c, 0.0f, hc_bytes);
    return vad;
}

__declspec(dllexport) void release_silero(struct SileroVAD* vad)
{
    vad->api->ReleaseEnv(vad->env);
    vad->api->ReleaseSessionOptions(vad->session_options);
    vad->api->ReleaseSession(vad->session);
    vad->api->ReleaseMemoryInfo(vad->memory_info);
    free(vad->h);
    free(vad->c);
    free(vad);
}

/**
 * Detect if speech is found for the given pcm data. This assumes the following:
 * - Pcm data contains a single frame containing 30ms of audio.
 * - Sample rate is 16khz. If the audio isn't 16khz, this will result in undefined behaviour.
 * 
 * Error handling is not present, as I assume the data sent from F# is already valid.
 */
__declspec(dllexport) float detect_speech(struct SileroVAD* vad, const float* pcm_data, const int pcm_data_length)
{
    // Input tensor (containing the pcm data).
    OrtValue* input_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        (float*) pcm_data,
        pcm_data_length,
        input_shape,
        sizeof(input_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &input_tensor
    );

    // Sample-rate tensor (assumes 16khz).
    int64_t sample_rate[] = {16000};
    OrtValue* sr_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        sample_rate,
        sizeof(int64_t),
        sr_shape,
        sizeof(sr_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_INT64,
        &sr_tensor
    );

    // h tensor.
    OrtValue* h_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->h,
        HC_LENGTH * sizeof(float),
        hc_shape,
        sizeof(hc_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &h_tensor
    );

    // c tensor.
    OrtValue* c_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->c,
        HC_LENGTH * sizeof(float),
        hc_shape,
        sizeof(hc_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &c_tensor
    );

    // Run inference. TODO: Make this async
    const OrtValue* input_tensors[] = {input_tensor, sr_tensor, h_tensor, c_tensor};
    OrtValue* output_tensors[] = {NULL, NULL, NULL};
    OrtStatusPtr status = vad->api->Run(
        vad->session,
        NULL,
        input_names,
        input_tensors,
        sizeof(input_tensors) / sizeof(OrtValue*),
        output_names,
        sizeof(output_names) / sizeof(char*),
        output_tensors
    );

    float* probabilities = NULL;
    vad->api->GetTensorMutableData(output_tensors[0], (void**) &probabilities);
    float* h_output = NULL;
    float* c_output = NULL;
    vad->api->GetTensorMutableData(output_tensors[1], (void**) &h_output);
    vad->api->GetTensorMutableData(output_tensors[2], (void**) &c_output);
    memcpy(vad->h, h_output, HC_LENGTH * sizeof(float));
    memcpy(vad->c, c_output, HC_LENGTH * sizeof(float));

    vad->api->ReleaseValue(input_tensor);
    vad->api->ReleaseValue(sr_tensor);
    vad->api->ReleaseValue(h_tensor);
    vad->api->ReleaseValue(c_tensor);

    return probabilities[0];
}

int main(int argc, char *argv[])
{
    printf("ok\n");
    struct SileroInitParams init_params;
    init_params.model_path = L"model/silero-vad/silero_vad.onnx";
    init_params.inter_threads = 1;
    init_params.intra_threads = 1;
    init_params.log_level = ORT_LOGGING_LEVEL_ERROR;
    struct SileroVAD* vad = init_silero(init_params);
    int pcm_data_length = 16000 * 3;
    float* pcm_data = (float*) malloc(pcm_data_length * sizeof(float));
    memset(pcm_data, 0.0f, pcm_data_length);
    float probability = detect_speech(vad, pcm_data, pcm_data_length);
    //std::cout << "input_wav length: " << sizeof(input_wav.data()) << std::endl;
    //float probability = detect_speech(vad, input_wav.data(), input_wav.size());
    printf("detected speech probability: %f", probability);
    release_silero(vad);
    return 0;
}