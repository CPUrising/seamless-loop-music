#pragma once

#include <stdint.h>

// ---- Windows DLL export / import ----
#ifdef _WIN32
  #ifdef LOOPFINDER_EXPORTS
    #define LOOPFINDER_API __declspec(dllexport)
  #else
    #define LOOPFINDER_API __declspec(dllimport)
  #endif
#else
  #define LOOPFINDER_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int64_t loopStart;
    int64_t loopEnd;
    float   noteDiff;
    float   loudnessDiff;
    float   score;
} lf_loop_point_t;

// 纯信号处理的节奏/情绪物理特征（无机器学习输出）
typedef struct {
    double  bpm;
    double  bpmConfidence;
    double  onsetDensity;
    double  rmsEnergy;
    double  spectralCentroid;
    int64_t durationSamples;
    int     sampleRate;
} lf_track_features_t;

// 窄字符入口（ANSI 编码路径，Android JNI 使用）
LOOPFINDER_API int lf_analyze_file(
    const char* filepath,
    int topN,
    lf_loop_point_t* outPoints,
    int capacity
);

#ifdef _WIN32
// 宽字符入口（Unicode 路径，不受系统 ANSI 代码页限制）
LOOPFINDER_API int lf_analyze_file_w(
    const wchar_t* filepath,
    int topN,
    lf_loop_point_t* outPoints,
    int capacity
);

// 宽字符分类特征入口：成功返回 0 并填充 outFeatures，失败返回负值
LOOPFINDER_API int lf_classify_file_w(
    const wchar_t* filepath,
    lf_track_features_t* outFeatures
);
#endif

LOOPFINDER_API const char* lf_get_last_error();

#ifdef __cplusplus
}
#endif
