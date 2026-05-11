#include "loopfinder/loop_finder.h"
#include "loopfinder/beat_detector.h"
#include "loopfinder/chroma.h"
#include "loopfinder/hpss.h"
#include "loopfinder/stft.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <numeric>

#ifdef LOOPFINDER_DEBUG
#define LF_LOG(fmt, ...) fprintf(stderr, fmt "\n", ##__VA_ARGS__)
#else
#define LF_LOG(fmt, ...) ((void)0)
#endif

namespace loopfinder {

// ---- Utility functions ----

float LoopFinder::cosineSimilarity(const float* a, const float* b, int len) {
    float dot = 0.0f, normA = 0.0f, normB = 0.0f;
    for (int i = 0; i < len; ++i) {
        dot   += a[i] * b[i];
        normA += a[i] * a[i];
        normB += b[i] * b[i];
    }
    float denom = std::sqrt(normA) * std::sqrt(normB);
    return (denom > 1e-10f) ? (dot / denom) : 0.0f;
}

float LoopFinder::vectorNorm(const float* v, int len) {
    float sum = 0.0f;
    for (int i = 0; i < len; ++i)
        sum += v[i] * v[i];
    return std::sqrt(sum);
}

void LoopFinder::geometricWeights(int len, float* weights, float start, float stop) {
    if (len <= 0) return;
    float ratio = std::pow(stop / start, 1.0f / (len - 1));
    weights[0] = start;
    for (int i = 1; i < len; ++i)
        weights[i] = weights[i - 1] * ratio;
}

// ---- Candidate pair enumeration ----

void LoopFinder::findCandidatePairs(const std::vector<std::vector<float>>& chroma,
                                    const std::vector<std::vector<float>>& powerDB,
                                    const std::vector<int>& beats,
                                    int minLoopFrames, int maxLoopFrames,
                                    std::vector<LoopPoint>& candidates) {
    candidates.clear();
    LF_LOG("[findCandidatePairs] beats=%zu minLoop=%d maxLoop=%d", beats.size(), minLoopFrames, maxLoopFrames);
    if (beats.size() < 2) return;

    const float ACCEPTABLE_NOTE_DEVIATION = 0.0875f;
    const float ACCEPTABLE_LOUDNESS_DIFF   = 0.5f;

    int numBeats = static_cast<int>(beats.size());
    int numChromaFrames = chroma.empty() ? 0 : static_cast<int>(chroma[0].size());
    LF_LOG("[findCandidatePairs] chroma=%zux%d", chroma.size(), numChromaFrames);

    std::vector<float> chromaNormPerBeat(numBeats);
    std::vector<float> maxPowerPerBeat(numBeats);

    int numPowerDBFrames = powerDB.empty() ? 0 : static_cast<int>(powerDB[0].size());
    LF_LOG("[findCandidatePairs] powerDB=%zux%d", powerDB.size(), numPowerDBFrames);

    for (int i = 0; i < numBeats; ++i) {
        int frame = beats[i];
        if (frame < 0 || frame >= numChromaFrames) {
            LF_LOG("[findCandidatePairs] WARN beat[%d]=%d out of chroma range [0,%d)", i, frame, numChromaFrames);
            continue;
        }
        maxPowerPerBeat[i] = 0.0f;
        for (int f = 0; f < 12; ++f) {
            chromaNormPerBeat[i] += chroma[f][frame] * chroma[f][frame];
        }
        chromaNormPerBeat[i] = std::sqrt(chromaNormPerBeat[i]);

        if (frame >= 0 && frame < numPowerDBFrames) {
            for (size_t f = 0; f < powerDB.size(); ++f) {
                maxPowerPerBeat[i] = std::max(maxPowerPerBeat[i],
                    std::abs(powerDB[f][frame]));
            }
        }
    }

    float avgNorm = 0.0f;
    for (float n : chromaNormPerBeat) avgNorm += n;
    avgNorm /= numBeats;

    float deviationThreshold = ACCEPTABLE_NOTE_DEVIATION * std::max(avgNorm, 1e-6f);
    LF_LOG("[findCandidatePairs] avgNorm=%.6f threshold=%.6f", avgNorm, deviationThreshold);

    for (int endIdx = 0; endIdx < numBeats; ++endIdx) {
        int loopEnd = beats[endIdx];
        for (int startIdx = 0; startIdx < endIdx; ++startIdx) {
            int loopStart = beats[startIdx];
            int loopLen = loopEnd - loopStart;

            if (loopLen > maxLoopFrames) continue;
            if (loopLen < minLoopFrames) break;

            float noteDist = 0.0f;
            for (int c = 0; c < 12; ++c) {
                float diff = chroma[c][loopEnd] - chroma[c][loopStart];
                noteDist += diff * diff;
            }
            noteDist = std::sqrt(noteDist);

            if (noteDist > deviationThreshold) continue;

            float loudnessDiff = std::abs(maxPowerPerBeat[endIdx] - maxPowerPerBeat[startIdx]);
            if (loudnessDiff > ACCEPTABLE_LOUDNESS_DIFF) continue;

            LoopPoint lp;
            lp.loopStart    = loopStart;
            lp.loopEnd      = loopEnd;
            lp.noteDiff     = noteDist;
            lp.loudnessDiff = loudnessDiff;
            lp.score        = 0.0f;
            candidates.push_back(lp);
        }
    }
    LF_LOG("[findCandidatePairs] found %zu candidates", candidates.size());
}

// ---- Cosine similarity scoring ----

void LoopFinder::scoreCandidates(const std::vector<std::vector<float>>& chroma,
                                 float bpm, int nFFT, int hopSize,
                                 std::vector<LoopPoint>& candidates) {
    LF_LOG("[scoreCandidates] candidates=%zu bpm=%.1f", candidates.size(), bpm);
    if (candidates.empty()) return;

    int numChromaFrames = static_cast<int>(chroma[0].size());

    float beatsPerSec = bpm / 60.0f;
    int numTestBeats = 12;
    float secondsToTest = numTestBeats / beatsPerSec;
    int testOffsetFrames = static_cast<int>(secondsToTest * nFFT / hopSize);
    if (testOffsetFrames > numChromaFrames)
        testOffsetFrames = numChromaFrames / 4;
    if (testOffsetFrames < 2) testOffsetFrames = 2;
    LF_LOG("[scoreCandidates] testOffsetFrames=%d", testOffsetFrames);

    if (candidates.size() >= 100) {
        std::sort(candidates.begin(), candidates.end(),
                  [](const LoopPoint& a, const LoopPoint& b) {
                      return (a.noteDiff + a.loudnessDiff) < (b.noteDiff + b.loudnessDiff);
                  });
        size_t keep = std::max(size_t(25), candidates.size() / 2);
        candidates.resize(std::min(keep, candidates.size()));
        LF_LOG("[scoreCandidates] pruned to %zu", candidates.size());
    }

    std::vector<float> weights(testOffsetFrames);
    geometricWeights(testOffsetFrames, weights.data(), 100.0f, 1.0f);

    int candIdx = 0;
    for (auto& lp : candidates) {
        int b1 = lp.loopStart;
        int b2 = lp.loopEnd;

        if (b1 < 0 || b1 >= numChromaFrames || b2 < 0 || b2 >= numChromaFrames) {
            LF_LOG("[scoreCandidates] WARN cand[%d] b1=%d b2=%d out of [0,%d)", candIdx, b1, b2, numChromaFrames);
            candIdx++;
            continue;
        }

        int b1End = std::min(b1 + testOffsetFrames, numChromaFrames);
        int b2End = std::min(b2 + testOffsetFrames, numChromaFrames);
        int maxOffset = std::min(b1End - b1, b2End - b2);

        std::vector<float> cosineSimLookahead(maxOffset);
        for (int i = 0; i < maxOffset; ++i) {
            float chromaA[12], chromaB[12];
            for (int c = 0; c < 12; ++c) {
                chromaA[c] = chroma[c][b1 + i];
                chromaB[c] = chroma[c][b2 + i];
            }
            cosineSimLookahead[i] = cosineSimilarity(chromaA, chromaB, 12);
        }

        float lookaheadScore = 0.0f;
        if (maxOffset > 0) {
            if (maxOffset < testOffsetFrames) {
                std::vector<float> padded(testOffsetFrames, 0.0f);
                for (int i = 0; i < maxOffset; ++i) padded[i] = cosineSimLookahead[i];
                float wSum = 0.0f;
                for (int i = 0; i < testOffsetFrames; ++i) {
                    lookaheadScore += padded[i] * weights[i];
                    wSum += weights[i];
                }
                lookaheadScore /= wSum;
            } else {
                float wSum = 0.0f;
                for (int i = 0; i < maxOffset; ++i) {
                    lookaheadScore += cosineSimLookahead[i] * weights[i];
                    wSum += weights[i];
                }
                lookaheadScore /= wSum;
            }
        }

        int b1Start = std::max(0, b1 - testOffsetFrames);
        int b2Start = std::max(0, b2 - testOffsetFrames);
        int maxNegOffset = std::min(b1 - b1Start, b2 - b2Start);

        float lookbehindScore = 0.0f;
        if (maxNegOffset > 0) {
            std::vector<float> revWeights(testOffsetFrames);
            geometricWeights(testOffsetFrames, revWeights.data(), 100.0f, 1.0f);
            std::reverse(revWeights.begin(), revWeights.end());

            std::vector<float> cosineSimLookbehind(maxNegOffset);
            for (int i = 0; i < maxNegOffset; ++i) {
                float chromaA[12], chromaB[12];
                for (int c = 0; c < 12; ++c) {
                    chromaA[c] = chroma[c][b1Start + i];
                    chromaB[c] = chroma[c][b2Start + i];
                }
                cosineSimLookbehind[i] = cosineSimilarity(chromaA, chromaB, 12);
            }

            if (maxNegOffset < testOffsetFrames) {
                std::vector<float> padded(testOffsetFrames, 0.0f);
                for (int i = 0; i < maxNegOffset; ++i) padded[i] = cosineSimLookbehind[i];
                float wSum = 0.0f;
                for (int i = 0; i < testOffsetFrames; ++i) {
                    lookbehindScore += padded[i] * revWeights[i];
                    wSum += revWeights[i];
                }
                lookbehindScore /= wSum;
            } else {
                float wSum = 0.0f;
                for (int i = 0; i < maxNegOffset; ++i) {
                    lookbehindScore += cosineSimLookbehind[i] * revWeights[i];
                    wSum += revWeights[i];
                }
                lookbehindScore /= wSum;
            }
        }

        lp.score = std::max(lookaheadScore, lookbehindScore);
        candIdx++;
    }

    std::sort(candidates.begin(), candidates.end(),
              [](const LoopPoint& a, const LoopPoint& b) {
                  return a.score > b.score;
              });
    LF_LOG("[scoreCandidates] done topScore=%.4f",
           candidates.empty() ? 0.0f : candidates[0].score);
}

// ---- Duration prioritization ----

void LoopFinder::prioritizeDuration(std::vector<LoopPoint>& candidates) {
    if (candidates.size() <= 1) return;

    std::vector<float> loudnessVals;
    for (auto& lp : candidates)
        loudnessVals.push_back(lp.loudnessDiff);
    std::sort(loudnessVals.begin(), loudnessVals.end());
    float dbThreshold = loudnessVals[loudnessVals.size() / 2];

    std::vector<float> scores;
    for (auto& lp : candidates)
        scores.push_back(lp.score);
    std::sort(scores.begin(), scores.end());
    float scoreThreshold = scores[static_cast<int>(scores.size() * 0.9)];
    scoreThreshold = std::max(scoreThreshold, candidates[0].score - 1e-4f);

    int bestDurationIdx = 0;
    int64_t bestDuration = 0;

    for (int i = 0; i < (int)candidates.size(); ++i) {
        if (candidates[i].score < scoreThreshold) break;
        int64_t dur = candidates[i].loopEnd - candidates[i].loopStart;
        if (dur > bestDuration && candidates[i].loudnessDiff <= dbThreshold) {
            bestDuration = dur;
            bestDurationIdx = i;
        }
    }

    if (bestDurationIdx > 0) {
        LoopPoint best = candidates[bestDurationIdx];
        candidates.erase(candidates.begin() + bestDurationIdx);
        candidates.insert(candidates.begin(), best);
    }
}

// ---- Main analysis pipeline ----

std::vector<LoopPoint> LoopFinder::analyze(const float* monoSignal, int signalLen,
                                           int sampleRate, const Config& config) {
    std::vector<LoopPoint> results;
    LF_LOG("[analyze] signalLen=%d sampleRate=%d nFFT=%d hopSize=%d",
           signalLen, sampleRate, config.nFFT, config.hopSize);

    // 1. STFT
    LF_LOG("[analyze] step 1: STFT init");
    STFT stft;
    if (!stft.init(config.nFFT, config.hopSize)) {
        fprintf(stderr, "[loopfinder] ERROR: STFT init failed\n");
        return results;
    }

    std::vector<std::vector<float>> powerSpec;
    int numFreqBins, numFrames;
    stft.computePower(monoSignal, signalLen, powerSpec, numFreqBins, numFrames);
    LF_LOG("[analyze] step 1 done: freqBins=%d frames=%d", numFreqBins, numFrames);

    // 2. HPSS
    LF_LOG("[analyze] step 2: HPSS");
    HPSS hpss;
    std::vector<std::vector<float>> harmonicSpec;
    hpss.harmonicOnly(powerSpec, harmonicSpec);
    LF_LOG("[analyze] step 2 done: harmonic %zux%zu", harmonicSpec.size(),
           harmonicSpec.empty() ? 0 : harmonicSpec[0].size());

    // 3. Chroma
    LF_LOG("[analyze] step 3: Chroma");
    ChromaExtractor chromaExtractor;
    std::vector<std::vector<float>> chromagram;
    chromaExtractor.extract(harmonicSpec, sampleRate, config.nFFT, chromagram);
    LF_LOG("[analyze] step 3 done: chroma %zux%zu", chromagram.size(),
           chromagram.empty() ? 0 : chromagram[0].size());

    // 4. Power DB
    LF_LOG("[analyze] step 4: powerDB");
    std::vector<std::vector<float>> powerDB(numFreqBins, std::vector<float>(numFrames, 0.0f));
    {
        std::vector<float> allVals;
        allVals.reserve(static_cast<size_t>(numFreqBins) * numFrames);
        for (int f = 0; f < numFreqBins; ++f)
            for (int t = 0; t < numFrames; ++t)
                if (powerSpec[f][t] > 1e-10f)
                    allVals.push_back(powerSpec[f][t]);
        LF_LOG("[analyze] step 4: median over %zu vals", allVals.size());

        float medianRef = 1.0f;
        if (!allVals.empty()) {
            std::sort(allVals.begin(), allVals.end());
            medianRef = allVals[allVals.size() / 2];
        }
        LF_LOG("[analyze] step 4: median=%.6f", medianRef);

        for (int k = 0; k < numFreqBins; ++k) {
            float freq = (k * sampleRate) / static_cast<float>(config.nFFT);
            float f2 = freq * freq;
            float numA = 12194.0f * 12194.0f * f2 * f2;
            float denA = (f2 + 20.6f * 20.6f) *
                         std::sqrt((f2 + 107.7f * 107.7f) * (f2 + 737.9f * 737.9f)) *
                         (f2 + 12194.0f * 12194.0f);
            float aWeight = (denA > 0) ? (numA / denA) : 0.0f;

            for (int t = 0; t < numFrames; ++t) {
                float val = powerSpec[k][t] * aWeight;
                powerDB[k][t] = 10.0f * std::log10(std::max(val, 1e-10f) / std::max(medianRef, 1e-10f));
            }
        }
    }
    LF_LOG("[analyze] step 4 done");

    // 5. Beat detection
    LF_LOG("[analyze] step 5: beat detection");
    BeatDetector beatDet;
    std::vector<int> beatFrames;
    float bpm = 120.0f;
    if (beatDet.init(config.hopSize, sampleRate)) {
        beatDet.detect(monoSignal, signalLen, beatFrames, bpm);
    } else {
        fprintf(stderr, "[loopfinder] WARN: aubio init failed, falling back to all frames\n");
        for (int i = 0; i < numFrames; ++i)
            beatFrames.push_back(i);
    }
    LF_LOG("[analyze] step 5 done: beats=%zu bpm=%.1f", beatFrames.size(), bpm);

    // 6. Loop duration constraints
    int totalFrames = stft.getNumFrames(signalLen);
    int minLoopFrames = static_cast<int>(config.minDurationMultiplier * totalFrames);
    if (config.minLoopDurationSec > 0.0f) {
        minLoopFrames = static_cast<int>(config.minLoopDurationSec * sampleRate / config.hopSize);
    }
    minLoopFrames = std::max(1, minLoopFrames);

    int maxLoopFrames = totalFrames;
    if (config.maxLoopDurationSec > 0.0f) {
        maxLoopFrames = static_cast<int>(config.maxLoopDurationSec * sampleRate / config.hopSize);
    }
    LF_LOG("[analyze] step 6: minLoopFrames=%d maxLoopFrames=%d", minLoopFrames, maxLoopFrames);

    // 7. Find candidate pairs
    LF_LOG("[analyze] step 7: findCandidatePairs");
    std::vector<LoopPoint> candidates;
    findCandidatePairs(chromagram, powerDB, beatFrames,
                       minLoopFrames, maxLoopFrames, candidates);

    if (candidates.empty()) {
        fprintf(stderr, "[loopfinder] no loop candidates found\n");
        return results;
    }

    // 8. Score candidates
    LF_LOG("[analyze] step 8: scoreCandidates");
    scoreCandidates(chromagram, bpm, config.nFFT, config.hopSize, candidates);

    // 9. Prioritize longer loops
    LF_LOG("[analyze] step 9: prioritizeDuration");
    prioritizeDuration(candidates);

    // 10. Convert frame indices to sample indices
    for (auto& lp : candidates) {
        lp.loopStart = static_cast<int64_t>(lp.loopStart) * config.hopSize;
        lp.loopEnd   = static_cast<int64_t>(lp.loopEnd) * config.hopSize;
    }

    // 11. Return top N
    int topN = std::min(config.topN, static_cast<int>(candidates.size()));
    results.assign(candidates.begin(), candidates.begin() + topN);
    LF_LOG("[analyze] DONE topN=%d topScore=%.4f", topN,
           results.empty() ? 0.0f : results[0].score);
    return results;
}

} // namespace loopfinder
