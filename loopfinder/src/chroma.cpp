#include "loopfinder/chroma.h"

#include <algorithm>
#include <cmath>

namespace loopfinder {

void ChromaExtractor::extract(const std::vector<std::vector<float>>& powerSpec,
                              int sampleRate, int nFFT,
                              std::vector<std::vector<float>>& chromagram) {
    numFreqBins_ = static_cast<int>(powerSpec.size());
    numFrames_   = static_cast<int>(powerSpec[0].size());

    chromagram.assign(NUM_CHROMA, std::vector<float>(numFrames_, 0.0f));

    std::vector<float> frqBins(nFFT, 0.0f);
    const float a440 = 440.0f;
    const float a0 = a440 / 16.0f;
    for (int k = 1; k < nFFT; ++k) {
        float freq = (k * sampleRate) / static_cast<float>(nFFT);
        frqBins[k] = NUM_CHROMA * std::log2(freq / a0);
    }
    frqBins[0] = frqBins[1] - 1.5f * NUM_CHROMA;

    std::vector<float> binWidths(nFFT, 1.0f);
    for (int k = 0; k < nFFT - 1; ++k)
        binWidths[k] = std::max(frqBins[k + 1] - frqBins[k], 1.0f);

    for (int k = 0; k < numFreqBins_; ++k) {
        float weights[NUM_CHROMA];
        float norm2 = 0.0f;
        for (int c = 0; c < NUM_CHROMA; ++c) {
            float d = frqBins[k] - static_cast<float>(c);
            d = std::fmod(d + 6.0f + 10.0f * NUM_CHROMA, static_cast<float>(NUM_CHROMA)) - 6.0f;
            float ratio = 2.0f * d / binWidths[k];
            float w = std::exp(-0.5f * ratio * ratio);
            weights[c] = w;
            norm2 += w * w;
        }

        float invNorm = (norm2 > 1e-20f) ? 1.0f / std::sqrt(norm2) : 0.0f;
        float octave = frqBins[k] / NUM_CHROMA;
        float octaveWeight = std::exp(-0.5f * std::pow((octave - 5.0f) / 2.0f, 2.0f));

        for (int c = 0; c < NUM_CHROMA; ++c) {
            int target = (c + NUM_CHROMA - 3) % NUM_CHROMA;
            float weight = weights[c] * invNorm * octaveWeight;
            if (weight <= 1e-20f) continue;
            for (int t = 0; t < numFrames_; ++t)
                chromagram[target][t] += weight * powerSpec[k][t];
        }
    }

    // librosa.feature.chroma_stft defaults to infinity-norm normalization.
    for (int t = 0; t < numFrames_; ++t) {
        float maxVal = 0.0f;
        for (int c = 0; c < NUM_CHROMA; ++c)
            maxVal = std::max(maxVal, chromagram[c][t]);
        if (maxVal > 1e-10f) {
            float invMax = 1.0f / maxVal;
            for (int c = 0; c < NUM_CHROMA; ++c)
                chromagram[c][t] *= invMax;
        }
    }
}

} // namespace loopfinder
