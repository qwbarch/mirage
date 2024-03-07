#include "onnxruntime_c_api.h"
#include <windows.h>

#define SR_SHAPE_LENGTH 1
#define HC_SHAPE_LENGTH 3
#define HC_TENSOR_LENGTH (2 * 1 * 64)
#define INPUT_LENGTH 4
#define OUTPUT_LENGTH 3

struct SileroVAD
{
    const OrtApi* api;
    OrtEnv* env;
    OrtSessionOptions* session_options;
    OrtSession* session;
    OrtAllocator* allocator;
    OrtMemoryInfo* memory_info;
    const char* log_id;
    float* h;
    float* c;
    int64_t* sample_rate;
    int64_t* sr_shape;
    int64_t* hc_shape;
    const char** input_names;
    const char** output_names;
};

struct SileroInitParams
{
    HMODULE onnxruntime;
    wchar_t* model_path;
    int inter_threads;
    int intra_threads;
    OrtLoggingLevel log_level;
};

HMODULE init_onnxruntime()
{
    return LoadLibrary("onnxruntime.dll");
}

struct SileroVAD* init_silero(const struct SileroInitParams init_params)
{
    struct SileroVAD* vad = malloc(sizeof(struct SileroVAD));
    OrtApiBase* ort_api_base = (OrtApiBase*) GetProcAddress(init_params.onnxruntime, "OrtGetApiBase")();
    vad->api = ort_api_base->GetApi(ORT_API_VERSION);
    vad->api->CreateEnv(init_params.log_level, vad->log_id, &vad->env);
    vad->api->CreateSessionOptions(&vad->session_options);
    vad->api->SetIntraOpNumThreads(vad->session_options, init_params.intra_threads);
    vad->api->SetInterOpNumThreads(vad->session_options, init_params.inter_threads);
    vad->api->SetSessionGraphOptimizationLevel(vad->session_options, ORT_ENABLE_ALL);
    vad->api->CreateSession(vad->env, init_params.model_path, vad->session_options, &vad->session);
    vad->api->CreateCpuMemoryInfo(OrtArenaAllocator, OrtMemTypeDefault, &vad->memory_info);
    vad->api->CreateAllocator(vad->session, vad->memory_info, &vad->allocator);
    vad->h = malloc(HC_TENSOR_LENGTH * sizeof(float));
    vad->c = malloc(HC_TENSOR_LENGTH * sizeof(float));
    memset(vad->h, 0.0f, HC_TENSOR_LENGTH * sizeof(float));
    memset(vad->c, 0.0f, HC_TENSOR_LENGTH * sizeof(float));
    vad->sample_rate = malloc(sizeof(int64_t));
    vad->sr_shape = malloc(sizeof(int64_t));
    vad->sr_shape[0] = 1;
    vad->hc_shape = malloc(HC_SHAPE_LENGTH * sizeof(int64_t));
    vad->hc_shape[0] = 2;
    vad->hc_shape[1] = 1;
    vad->hc_shape[2] = 64;
    vad->input_names = malloc(INPUT_LENGTH * sizeof(char*));
    vad->input_names[0] = "input";
    vad->input_names[1] = "sr";
    vad->input_names[2] = "h";
    vad->input_names[3] = "c";
    vad->output_names = malloc(OUTPUT_LENGTH * sizeof(char*));
    vad->output_names[0] = "output";
    vad->output_names[1] = "hn";
    vad->output_names[2] = "cn";
    return vad;
}

void release_silero(struct SileroVAD* vad)
{
    vad->api->ReleaseEnv(vad->env);
    vad->api->ReleaseSessionOptions(vad->session_options);
    vad->api->ReleaseSession(vad->session);
    vad->api->ReleaseAllocator(vad->allocator);
    vad->api->ReleaseMemoryInfo(vad->memory_info);
    free(vad->h);
    free(vad->c);
    free(vad->sample_rate);
    free(vad->sr_shape);
    free(vad->hc_shape);
    free(vad->input_names);
    free(vad->output_names);
    free(vad);
}

/**
 * Detect if speech is found for the given pcm data. This assumes the following:
 * - Pcm data contains 30ms of audio.
 * - Sample rate is 16khz. If the audio isn't 16khz, this will result in undefined behaviour.
 * 
 * Error handling is not present, as I assume the data sent from F# is already valid.
 */
float is_speech_detected(struct SileroVAD* vad, float* pcm_data, int pcm_data_length)
{
    // Input (pcm data) tensor.
    int64_t* input_shape = malloc(2 * sizeof(long));
    input_shape[0] = 1L;
    input_shape[1] = (long) pcm_data_length;
    OrtValue* input_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        pcm_data,
        pcm_data_length,
        input_shape,
        2,
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &input_tensor
    );

    // Sample rate tensor (assumes 16khz sample rate).
    OrtValue* sr_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->sample_rate,
        sizeof(int64_t),
        vad->sr_shape,
        SR_SHAPE_LENGTH,
        ONNX_TENSOR_ELEMENT_DATA_TYPE_INT64,
        &sr_tensor
    );

    // h tensor.
    OrtValue* h_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->h,
        HC_TENSOR_LENGTH,
        vad->hc_shape,
        HC_SHAPE_LENGTH,
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &h_tensor
    );

    // c tensor.
    OrtValue* c_tensor = NULL;
    vad->api->CreateTensorWithDataAsOrtValue(
        vad->memory_info,
        vad->c,
        HC_TENSOR_LENGTH,
        vad->hc_shape,
        HC_SHAPE_LENGTH,
        ONNX_TENSOR_ELEMENT_DATA_TYPE_FLOAT,
        &c_tensor
    );

    // Run inference.
    OrtValue* inputs[] = {input_tensor, sr_tensor, h_tensor, c_tensor};
    OrtValue* outputs[] = {NULL, NULL, NULL};
    vad->api->Run(
        vad->session,
        NULL,
        vad->input_names,
        inputs,
        INPUT_LENGTH,
        vad->output_names,
        OUTPUT_LENGTH,
        outputs
    );

    // Extract the speech detection probabilities.
    float* probabilities = NULL;
    vad->api->GetTensorMutableData(outputs[0], &probabilities);

    // Update the h/n tensors.
    float* h_output = NULL;
    float* c_output = NULL;
    vad->api->GetTensorMutableData(outputs[1], &h_output);
    vad->api->GetTensorMutableData(outputs[2], &c_output);
    memcpy(vad->h, h_output, HC_TENSOR_LENGTH * sizeof(float));
    memcpy(vad->c, c_output, HC_TENSOR_LENGTH * sizeof(float));

    // Free resources.
    vad->api->ReleaseValue(input_tensor);
    vad->api->ReleaseValue(sr_tensor);
    vad->api->ReleaseValue(h_tensor);
    vad->api->ReleaseValue(c_tensor);
    free(input_shape);
    free(inputs);
    free(outputs);
    free(h_output);
    free(c_output);
    return probabilities[0];
}