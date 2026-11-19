#pragma once

#include <cstdint>

namespace loopfinder {

// 纯信号处理的节奏/情绪物理特征，不含任何机器学习输出
struct TrackFeatures {
    double bpm             = 0.0;  // aubio tempo 估算的每分钟节拍数
    double bpmConfidence   = 0.0;  // aubio tempo 置信度（越高越可靠）
    double onsetDensity    = 0.0;  // 每秒起始（onset/节拍事件）数量
    double rmsEnergy       = 0.0;  // 整曲 RMS 能量 [0,1]
    double spectralCentroid = 0.0; // 频谱质心（Hz），衡量"亮度"
    int64_t durationSamples = 0;   // 采样点数
    int     sampleRate      = 0;
};

class FeatureExtractor {
public:
    FeatureExtractor() = default;

    // 对单声道 PCM 提取特征；失败返回 false
    bool extract(const float* monoSignal, int signalLen, int sampleRate,
                 TrackFeatures& out);
};

} // namespace loopfinder
