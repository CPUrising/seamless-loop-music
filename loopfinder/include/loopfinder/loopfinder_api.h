#pragma once

#include <stdint.h>

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

int lf_analyze_file(
    const char* filepath,
    int topN,
    lf_loop_point_t* outPoints,
    int capacity
);

const char* lf_get_last_error();

#ifdef __cplusplus
}
#endif
