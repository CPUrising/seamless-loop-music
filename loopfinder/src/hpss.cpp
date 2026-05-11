#include "loopfinder/hpss.h"

#include <algorithm>
#include <cmath>
#include <vector>

namespace loopfinder {

static void medianFilter2DHorizontal(const std::vector<std::vector<float>>& src,
                                     std::vector<std::vector<float>>& dst,
                                     int halfKernel) {
    int numFreqBins = static_cast<int>(src.size());
    int numFrames   = static_cast<int>(src[0].size());
    dst.assign(numFreqBins, std::vector<float>(numFrames, 0.0f));

    std::vector<float> window;
    window.reserve(2 * halfKernel + 1);

    for (int f = 0; f < numFreqBins; ++f) {
        for (int t = 0; t < numFrames; ++t) {
            window.clear();
            for (int dt = -halfKernel; dt <= halfKernel; ++dt) {
                int tt = t + dt;
                if (tt >= 0 && tt < numFrames)
                    window.push_back(src[f][tt]);
            }
            if (!window.empty()) {
                std::sort(window.begin(), window.end());
                dst[f][t] = window[window.size() / 2];
            }
        }
    }
}

static void medianFilter2DVertical(const std::vector<std::vector<float>>& src,
                                   std::vector<std::vector<float>>& dst,
                                   int halfKernel) {
    int numFreqBins = static_cast<int>(src.size());
    int numFrames   = static_cast<int>(src[0].size());
    dst.assign(numFreqBins, std::vector<float>(numFrames, 0.0f));

    std::vector<float> window;
    window.reserve(2 * halfKernel + 1);

    for (int t = 0; t < numFrames; ++t) {
        for (int f = 0; f < numFreqBins; ++f) {
            window.clear();
            for (int df = -halfKernel; df <= halfKernel; ++df) {
                int ff = f + df;
                if (ff >= 0 && ff < numFreqBins)
                    window.push_back(src[ff][t]);
            }
            if (!window.empty()) {
                std::sort(window.begin(), window.end());
                dst[f][t] = window[window.size() / 2];
            }
        }
    }
}

void HPSS::separate(const std::vector<std::vector<float>>& powerSpec,
                    std::vector<std::vector<float>>& harmonic,
                    std::vector<std::vector<float>>& percussive,
                    int kernelSize) {
    int halfKernel = kernelSize / 2;

    std::vector<std::vector<float>> hRaw, pRaw;
    medianFilter2DHorizontal(powerSpec, hRaw, halfKernel);
    medianFilter2DVertical(powerSpec, pRaw, halfKernel);

    int numFreqBins = static_cast<int>(powerSpec.size());
    int numFrames   = static_cast<int>(powerSpec[0].size());

    harmonic.assign(numFreqBins, std::vector<float>(numFrames, 0.0f));
    percussive.assign(numFreqBins, std::vector<float>(numFrames, 0.0f));

    for (int f = 0; f < numFreqBins; ++f) {
        for (int t = 0; t < numFrames; ++t) {
            float h = hRaw[f][t];
            float p = pRaw[f][t];
            float denom = h * h + p * p;
            if (denom > 1e-12f) {
                harmonic[f][t]   = (h * h / denom) * powerSpec[f][t];
                percussive[f][t] = (p * p / denom) * powerSpec[f][t];
            }
        }
    }
}

void HPSS::harmonicOnly(std::vector<std::vector<float>>& powerSpec,
                        std::vector<std::vector<float>>& harmonic,
                        int kernelSize) {
    std::vector<std::vector<float>> percussive;
    separate(powerSpec, harmonic, percussive, kernelSize);
}

} // namespace loopfinder
