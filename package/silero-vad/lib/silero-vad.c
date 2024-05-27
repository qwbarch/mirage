#include "../../../lib/onnxruntime/include/onnxruntime_c_api.h"
#include <wtypes.h>

#define HC_LENGTH 2 * 1 * 64

const int64_t sr_shape[] = {1};
const int64_t hc_shape[] = {2, 1, 64};

const char *input_names[] = {"input", "sr", "h", "c"};
const char *output_names[] = {"output", "hn", "cn"};

struct SileroVAD
{
    const OrtApi *api;
    OrtEnv *env;
    OrtSessionOptions *session_options;
    OrtSession *session;
    OrtMemoryInfo *memory_info;
    float *h;
    float *c;
    int64_t input_shape[2];
};

struct SileroInitParams
{
    const wchar_t *onnxruntime_path;
    const wchar_t *model_path;
    OrtLoggingLevel log_level;
    int intra_threads;
    int inter_threads;
    int window_size;
};

typedef OrtApiBase *(ORT_API_CALL *OrtGetApiBaseFunc)();

__declspec(dllexport) struct SileroVAD *init_silero(struct SileroInitParams init_params)
{
    struct SileroVAD *vad = malloc(sizeof(struct SileroVAD));
    HMODULE onnxruntime = LoadLibraryW(init_params.onnxruntime_path);
    OrtGetApiBaseFunc OrtGetApiBase = (OrtGetApiBaseFunc)GetProcAddress(onnxruntime, "OrtGetApiBase");
    vad->api = OrtGetApiBase()->GetApi(ORT_API_VERSION);
    vad->api->CreateEnv(init_params.log_level, "Mirage", &vad->env);
    vad->api->CreateSessionOptions(&vad->session_options);
    vad->api->SetIntraOpNumThreads(vad->session_options, init_params.intra_threads);
    vad->api->SetInterOpNumThreads(vad->session_options, init_params.inter_threads);
    vad->api->SetSessionGraphOptimizationLevel(vad->session_options, ORT_ENABLE_ALL);
    vad->api->CreateSession(vad->env, init_params.model_path, vad->session_options, &vad->session);
    vad->api->CreateCpuMemoryInfo(OrtArenaAllocator, OrtMemTypeCPU, &vad->memory_info);
    const int hc_bytes = HC_LENGTH * sizeof(float);
    vad->h = (float *)malloc(hc_bytes);
    vad->c = (float *)malloc(hc_bytes);
    memset(vad->h, 0.0f, hc_bytes);
    memset(vad->c, 0.0f, hc_bytes);
    vad->input_shape[0] = 1;
    vad->input_shape[1] = init_params.window_size;
    return vad;
}

__declspec(dllexport) void release_silero(struct SileroVAD *vad)
{
    vad->api->ReleaseEnv(vad->env);
    vad->api->ReleaseSessionOptions(vad->session_options);
    vad->api->ReleaseSession(vad->session);
    vad->api->ReleaseMemoryInfo(vad->memory_info);
    free(vad->h);
    free(vad->c);
    free(vad->input_shape);
    free(vad);
}

/**
 * Detect if speech is found for the given pcm data. This assumes the following:
 * - Pcm data contains WINDOW_SIZE samples.
 * - Sample rate is 16khz.
 * - Audio is mono-channel.
 * - Each sample contains 2 bytes.
 *
 * If the given pcm data does not follow this structure, its output will be unknown (and can potentially crash).
 * Error handling is not present, as I assume the data sent from F# is already valid.
 */
__declspec(dllexport) float detect_speech(struct SileroVAD *vad, const float *pcm_data, const int pcm_data_length)
{
    // Input tensor (containing the pcm data).
    OrtValue *input_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        (float *)pcm_data,
        pcm_data_length * sizeof(float),
        vad->input_shape,
        sizeof(vad->input_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &input_tensor);

    // Sample-rate tensor (assumes 16khz).
    int64_t sample_rate[] = {16000};
    OrtValue *sr_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        sample_rate,
        sizeof(int64_t),
        sr_shape,
        sizeof(sr_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_INT64,
        &sr_tensor);

    // h tensor.
    OrtValue *h_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->h,
        HC_LENGTH * sizeof(float),
        hc_shape,
        sizeof(hc_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &h_tensor);

    // c tensor.
    OrtValue *c_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->c,
        HC_LENGTH * sizeof(float),
        hc_shape,
        sizeof(hc_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &c_tensor);

    // Run inference.
    const OrtValue *input_tensors[] = {input_tensor, sr_tensor, h_tensor, c_tensor};
    OrtValue *output_tensors[] = {NULL, NULL, NULL};
    vad->api->Run(
        vad->session,
        NULL,
        input_names,
        input_tensors,
        sizeof(input_tensors) / sizeof(OrtValue *),
        output_names,
        sizeof(output_names) / sizeof(char *),
        output_tensors);

    float *probabilities = NULL;
    float *h_output = NULL;
    float *c_output = NULL;
    vad->api->GetTensorMutableData(output_tensors[0], &probabilities);
    vad->api->GetTensorMutableData(output_tensors[1], &h_output);
    vad->api->GetTensorMutableData(output_tensors[2], &c_output);

    memcpy(vad->h, h_output, HC_LENGTH * sizeof(float));
    memcpy(vad->c, c_output, HC_LENGTH * sizeof(float));

    vad->api->ReleaseValue(output_tensors[0]);
    vad->api->ReleaseValue(output_tensors[1]);
    vad->api->ReleaseValue(output_tensors[2]);
    vad->api->ReleaseValue(input_tensor);
    vad->api->ReleaseValue(sr_tensor);
    vad->api->ReleaseValue(h_tensor);
    vad->api->ReleaseValue(c_tensor);

    return probabilities[0];
}