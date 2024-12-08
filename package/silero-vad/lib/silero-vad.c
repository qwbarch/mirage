#include "../../../lib/onnxruntime/include/onnxruntime_c_api.h"
#include <wtypes.h>

#define STATE_LENGTH 2 * 1 * 128

const int64_t sr_shape[] = {1};
const int64_t state_shape[] = {2, 1, 128};

const char *input_names[] = {"input", "state", "sr"};
const char *output_names[] = {"output", "stateN"};

struct SileroVAD
{
    const OrtApi *api;
    OrtEnv *env;
    OrtSessionOptions *session_options;
    OrtSession *session;
    OrtMemoryInfo *memory_info;
    float *state;
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
    int state_bytes = STATE_LENGTH * sizeof(float);
    vad->state = (float *)malloc(state_bytes);
    memset(vad->state, 0.0f, state_bytes);
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
    free(vad->state);
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

    // State tensor.
    OrtValue *state_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->state,
        STATE_LENGTH * sizeof(float),
        state_shape,
        sizeof(state_shape) / sizeof(int64_t),
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &state_tensor);

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

    // Run inference.
    const OrtValue *input_tensors[] = {input_tensor, state_tensor, sr_tensor};
    OrtValue *output_tensors[] = {NULL, NULL};
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
    float *state_output = NULL;

    vad->api->GetTensorMutableData(output_tensors[0], &probabilities);
    vad->api->GetTensorMutableData(output_tensors[1], &state_output);

    memcpy(vad->state, state_output, STATE_LENGTH * sizeof(float));

    vad->api->ReleaseValue(output_tensors[0]);
    vad->api->ReleaseValue(output_tensors[1]);
    vad->api->ReleaseValue(input_tensor);
    vad->api->ReleaseValue(state_tensor);
    vad->api->ReleaseValue(sr_tensor);

    return probabilities[0];
}