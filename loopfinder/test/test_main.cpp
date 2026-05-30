#include <iostream>
#include <string>
#include <vector>

#include "loopfinder/loop_finder.h"
#include "loopfinder/audio_decoder.h"

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "usage: loopfinder_test audio.wav\n";
        return 1;
    }

    using namespace loopfinder;

    PCMData audio;
    AudioDecoder decoder;
    if (!decoder.decode(argv[1], audio)) {
        std::cerr << "decode failed: " << argv[1] << "\n";
        return 1;
    }

    std::cout << "loaded: " << argv[1]
              << "  sr=" << audio.sampleRate
              << "  samples=" << audio.samples.size()
              << "  trimStart=" << audio.trimStart << "\n";

    LoopFinder finder;
    LoopFinder::Config config;
    config.topN = 5;
    for (int i = 2; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg == "--no-hpss") config.useHPSS = false;
        if (arg == "--duration-priority") config.prioritizeDuration = true;
        if (arg.rfind("--grid=", 0) == 0) config.candidateFrameStep = std::stoi(arg.substr(7));
    }

    std::cout << "config: useHPSS=" << (config.useHPSS ? "true" : "false")
              << "  prioritizeDuration=" << (config.prioritizeDuration ? "true" : "false")
              << "  candidateFrameStep=" << config.candidateFrameStep
              << "\n";

    auto results = finder.analyze(
        audio.samples.data(),
        static_cast<int>(audio.samples.size()),
        audio.sampleRate,
        config);

    std::cout << "found " << results.size() << " loop points:\n";
    for (auto& r : results) {
        int64_t originalFrameStart =
            (static_cast<int64_t>(r.loopStartFrame) * config.hopSize + audio.trimStart) / config.hopSize;
        int64_t originalFrameEnd =
            (static_cast<int64_t>(r.loopEndFrame) * config.hopSize + audio.trimStart) / config.hopSize;
        std::cout << "  start=" << r.loopStart
                  << "  end=" << r.loopEnd
                  << "  frameStart=" << r.loopStartFrame
                  << "  frameEnd=" << r.loopEndFrame
                  << "  originalFrameStart=" << originalFrameStart
                  << "  originalFrameEnd=" << originalFrameEnd
                  << "  originalStart=" << (r.loopStart + audio.trimStart)
                  << "  originalEnd=" << (r.loopEnd + audio.trimStart)
                  << "  noteDiff=" << r.noteDiff
                  << "  loudnessDiff=" << r.loudnessDiff
                  << "  score=" << r.score << "\n";
    }

    return 0;
}
