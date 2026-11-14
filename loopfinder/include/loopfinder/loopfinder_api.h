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
#endif

LOOPFINDER_API const char* lf_get_last_error();

#ifdef __cplusplus
}
#endif
