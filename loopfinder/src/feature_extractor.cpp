#include "loopfinder/feature_extractor.h"
#include "loopfinder/stft.h"

#include "aubio.h"

#include <cmath>
#include <vector>

namespace loopfinder {

bool FeatureExtractor::extract(const float* monoSignal, int signalLen, int sampleRate,
                               TrackFeatures& out) {
    if (!monoSignal || signalLen <= 0 || sampleRate <= 0) return false;

    out.sampleRate      = sampleRate;
    out.durationSamples = signalLen;

    // ---- RMS 能量：直接对 PCM 求均方根 ----
    double sumSq = 0.0;
    for (int i = 0; i < signalLen; ++i) {
        double s = static_cast<double>(monoSignal[i]);
        sumSq += s * s;
    }
    out.rmsEnergy = std::sqrt(sumSq / signalLen);

    // ---- BPM / 置信度 / 起始密度：直接使用 aubio tempo ----
    {
        const uint_t hopSize = 512;
        const uint_t bufSize = 1024;
        aubio_tempo_t* tempo = new_aubio_tempo("default", bufSize, hopSize,
                                               static_cast<uint_t>(sampleRate));
        if (tempo) {
            fvec_t* in  = new_fvec(hopSize);
            fvec_t* o   = new_fvec(1);
            int onsetCount = 0;

            int totalHops = signalLen / static_cast<int>(hopSize);
            for (int i = 0; i < totalHops; ++i) {
                int offset = i * static_cast<int>(hopSize);
                int copyLen = (offset + static_cast<int>(hopSize) <= signalLen)
                                  ? static_cast<int>(hopSize)
                                  : (signalLen - offset);
                for (int j = 0; j < copyLen; ++j) in->data[j] = monoSignal[offset + j];
                for (int j = copyLen; j < static_cast<int>(hopSize); ++j) in->data[j] = 0.0f;

                aubio_tempo_do(tempo, in, o);
                if (o->data[0] > 0.0f) ++onsetCount;
            }

            out.bpm           = static_cast<double>(aubio_tempo_get_bpm(tempo));
            out.bpmConfidence = static_cast<double>(aubio_tempo_get_confidence(tempo));

            double durationSec = static_cast<double>(signalLen) / sampleRate;
            if (durationSec > 0.0) {
                out.onsetDensity = static_cast<double>(onsetCount) / durationSec;
            }

            del_fvec(in);
            del_fvec(o);
            del_aubio_tempo(tempo);
        }
    }

    // ---- 频谱质心：对整曲幅度谱做能量加权平均频率 ----
    {
        STFT stft;
        if (stft.init(2048, 1024)) {
            std::vector<std::vector<float>> mag;
            int numFreqBins = 0, numFrames = 0;
            stft.compute(monoSignal, signalLen, mag, numFreqBins, numFrames);

            double weightedSum = 0.0, totalMag = 0.0;
            double binHz = static_cast<double>(sampleRate) / 2048.0;
            for (int k = 0; k < numFreqBins; ++k) {
                double binMag = 0.0;
                for (int t = 0; t < numFrames; ++t) binMag += mag[k][t];
                if (binMag > 0.0) {
                    weightedSum += (k * binHz) * binMag;
                    totalMag    += binMag;
                }
            }
            if (totalMag > 0.0) out.spectralCentroid = weightedSum / totalMag;
        }
    }

    return true;
}

} // namespace loopfinder
