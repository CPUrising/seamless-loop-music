#include "loopfinder/audio_decoder.h"

#define DR_WAV_IMPLEMENTATION
#include "dr_wav.h"
#define DR_FLAC_IMPLEMENTATION
#include "dr_flac.h"
#define DR_MP3_IMPLEMENTATION
#include "dr_mp3.h"
#include "stb_vorbis.c"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <utility>

#ifdef _WIN32
#define strcasecmp _stricmp
#endif

namespace loopfinder {

namespace {

AudioFormat detectFormat(const char* filepath) {
    const char* ext = std::strrchr(filepath, '.');
    if (!ext) return AudioFormat::UNKNOWN;
    if (strcasecmp(ext, ".wav") == 0 || strcasecmp(ext, ".wave") == 0) return AudioFormat::WAV;
    if (strcasecmp(ext, ".flac") == 0) return AudioFormat::FLAC;
    if (strcasecmp(ext, ".mp3") == 0)  return AudioFormat::MP3;
    if (strcasecmp(ext, ".ogg") == 0)  return AudioFormat::OGG;
    return AudioFormat::UNKNOWN;
}

#ifdef _WIN32
AudioFormat detectFormat(const wchar_t* filepath) {
    const wchar_t* ext = std::wcsrchr(filepath, L'.');
    if (!ext) return AudioFormat::UNKNOWN;
    if (_wcsicmp(ext, L".wav") == 0 || _wcsicmp(ext, L".wave") == 0) return AudioFormat::WAV;
    if (_wcsicmp(ext, L".flac") == 0) return AudioFormat::FLAC;
    if (_wcsicmp(ext, L".mp3") == 0)  return AudioFormat::MP3;
    if (_wcsicmp(ext, L".ogg") == 0)  return AudioFormat::OGG;
    return AudioFormat::UNKNOWN;
}
#endif

void toMono(std::vector<float>& samples, int channels) {
    if (channels <= 1) return;
    int64_t numFrames = samples.size() / channels;
    for (int64_t i = 0; i < numFrames; ++i) {
        float sum = 0.0f;
        for (int ch = 0; ch < channels; ++ch)
            sum += samples[i * channels + ch];
        samples[i] = sum / channels;
    }
    samples.resize(numFrames);
}

void normalize(std::vector<float>& samples) {
    float maxAbs = 0.0f;
    for (float s : samples)
        maxAbs = std::max(maxAbs, std::abs(s));
    if (maxAbs > 1e-8f) {
        float invMax = 1.0f / maxAbs;
        for (float& s : samples) s *= invMax;
    }
}

void trimSilence(std::vector<float>& samples, int sampleRate, int& trimStart) {
    trimStart = 0;
    constexpr int frameLen = 2048;
    constexpr int hopLen = 512;
    constexpr float topDb = 40.0f;

    if (samples.empty()) return;

    int64_t numFrames = static_cast<int64_t>(samples.size()) / hopLen + 1;
    std::vector<float> rmsPerFrame(static_cast<size_t>(numFrames), 0.0f);
    float refRms = 0.0f;

    for (int64_t f = 0; f < numFrames; ++f) {
        float sumSq = 0.0f;
        int64_t start = f * hopLen - frameLen / 2;
        for (int i = 0; i < frameLen; ++i) {
            int64_t idx = start + i;
            if (idx >= 0 && idx < static_cast<int64_t>(samples.size()))
                sumSq += samples[static_cast<size_t>(idx)] * samples[static_cast<size_t>(idx)];
        }
        float rms = std::sqrt(sumSq / frameLen);
        rmsPerFrame[static_cast<size_t>(f)] = rms;
        refRms = std::max(refRms, rms);
    }

    if (refRms <= 1e-10f) return;

    float threshold = refRms * std::pow(10.0f, -topDb / 20.0f);

    int64_t startFrame = 0;
    while (startFrame < numFrames && rmsPerFrame[static_cast<size_t>(startFrame)] <= threshold)
        ++startFrame;

    int64_t endFrame = numFrames - 1;
    while (endFrame >= 0 && rmsPerFrame[static_cast<size_t>(endFrame)] <= threshold)
        --endFrame;

    if (startFrame >= endFrame) return;

    int64_t cutStart = startFrame * hopLen;
    int64_t samplesSize = static_cast<int64_t>(samples.size());
    int64_t cutEnd = std::min((endFrame + 1) * hopLen, samplesSize);

    trimStart = static_cast<int>(cutStart);
    std::vector<float> trimmed(samples.begin() + cutStart, samples.begin() + cutEnd);
    samples = std::move(trimmed);
}

// 统一收尾：单声道 / 归一化 / 去静音，窄字符与宽字符入口共用，避免两套实现漂移
void finalizeSamples(std::vector<float>&& samples, int channels, int sampleRate,
                     int64_t frames, PCMData& out) {
    if (channels > 1) toMono(samples, channels);
    if (static_cast<int64_t>(samples.size()) > frames)
        samples.resize(static_cast<size_t>(frames));

    out.sampleRate = sampleRate;
    out.numChannels = channels;
    out.totalSamples = frames;
    out.samples = std::move(samples);

    normalize(out.samples);
    trimSilence(out.samples, out.sampleRate, out.trimStart);
}

bool openWav(const char* path, drwav& wav) {
    return drwav_init_file(&wav, path, nullptr) != 0;
}

template <typename PathT>
bool decodeWavFile(const PathT* path, PCMData& out) {
    drwav wav;
    if (!openWav(path, wav)) return false;

    const int channels = wav.channels;
    const int sampleRate = static_cast<int>(wav.sampleRate);
    const int64_t frames = static_cast<int64_t>(wav.totalPCMFrameCount);

    std::vector<float> interleaved(static_cast<size_t>(frames) * static_cast<size_t>(channels));
    drwav_read_pcm_frames_f32(&wav, wav.totalPCMFrameCount, interleaved.data());
    drwav_uninit(&wav);

    finalizeSamples(std::move(interleaved), channels, sampleRate, frames, out);
    return true;
}

bool openFlac(const char* path, drflac*& flac) {
    flac = drflac_open_file(path, nullptr);
    return flac != nullptr;
}

template <typename PathT>
bool decodeFlacFile(const PathT* path, PCMData& out) {
    drflac* flac = nullptr;
    if (!openFlac(path, flac)) return false;

    const int channels = flac->channels;
    const int sampleRate = static_cast<int>(flac->sampleRate);
    const int64_t frames = static_cast<int64_t>(flac->totalPCMFrameCount);

    std::vector<float> interleaved(static_cast<size_t>(frames) * static_cast<size_t>(channels));
    drflac_read_pcm_frames_f32(flac, flac->totalPCMFrameCount, interleaved.data());
    drflac_close(flac);

    finalizeSamples(std::move(interleaved), channels, sampleRate, frames, out);
    return true;
}

bool openMp3(const char* path, drmp3& mp3) {
    return drmp3_init_file(&mp3, path, nullptr) != 0;
}

template <typename PathT>
bool decodeMp3File(const PathT* path, PCMData& out) {
    drmp3 mp3;
    if (!openMp3(path, mp3)) return false;

    const int channels = mp3.channels;
    const int sampleRate = static_cast<int>(mp3.sampleRate);
    const int64_t frames = static_cast<int64_t>(drmp3_get_pcm_frame_count(&mp3));

    std::vector<float> interleaved(static_cast<size_t>(frames) * static_cast<size_t>(channels));
    drmp3_read_pcm_frames_f32(&mp3, static_cast<drmp3_uint64>(frames), interleaved.data());
    drmp3_uninit(&mp3);

    finalizeSamples(std::move(interleaved), channels, sampleRate, frames, out);
    return true;
}

void finalizeOgg(short* raw, int numSamples, int channels, int sampleRate, PCMData& out) {
    std::vector<float> samples(static_cast<size_t>(numSamples));
    for (int i = 0; i < numSamples; ++i) {
        float sum = 0.0f;
        for (int ch = 0; ch < channels; ++ch)
            sum += raw[i * channels + ch] / 32768.0f;
        samples[i] = sum / channels;
    }
    finalizeSamples(std::move(samples), channels, sampleRate, numSamples, out);
}

bool decodeOggFile(const char* path, PCMData& out) {
    int channels = 0, sampleRate = 0;
    short* raw = nullptr;
    int numSamples = stb_vorbis_decode_filename(path, &channels, &sampleRate, &raw);
    if (numSamples < 0) return false;

    finalizeOgg(raw, numSamples, channels, sampleRate, out);
    free(raw);
    return true;
}

#ifdef _WIN32
bool openWav(const wchar_t* path, drwav& wav) {
    return drwav_init_file_w(&wav, path, nullptr) != 0;
}

bool openFlac(const wchar_t* path, drflac*& flac) {
    flac = drflac_open_file_w(path, nullptr);
    return flac != nullptr;
}

bool openMp3(const wchar_t* path, drmp3& mp3) {
    return drmp3_init_file_w(&mp3, path, nullptr) != 0;
}

// stb_vorbis 无宽字符文件名接口：先整文件读入内存，再按内存解码
bool decodeOggFile(const wchar_t* path, PCMData& out) {
    FILE* file = nullptr;
    if (_wfopen_s(&file, path, L"rb") != 0 || !file) return false;

    if (_fseeki64(file, 0, SEEK_END) != 0) {
        std::fclose(file);
        return false;
    }
    const long long size = _ftelli64(file);
    if (size <= 0 || size > INT32_MAX) {
        std::fclose(file);
        return false;
    }
    std::rewind(file);

    std::vector<unsigned char> bytes(static_cast<size_t>(size));
    const size_t read = std::fread(bytes.data(), 1, bytes.size(), file);
    std::fclose(file);
    if (read != bytes.size()) return false;

    int channels = 0, sampleRate = 0;
    short* raw = nullptr;
    int numSamples = stb_vorbis_decode_memory(
        bytes.data(), static_cast<int>(bytes.size()), &channels, &sampleRate, &raw);
    if (numSamples < 0) return false;

    finalizeOgg(raw, numSamples, channels, sampleRate, out);
    free(raw);
    return true;
}
#endif

} // namespace

AudioDecoder::~AudioDecoder() {}

bool AudioDecoder::decode(const char* filepath, PCMData& out) {
    switch (detectFormat(filepath)) {
        case AudioFormat::WAV:  return decodeWavFile(filepath, out);
        case AudioFormat::FLAC: return decodeFlacFile(filepath, out);
        case AudioFormat::MP3:  return decodeMp3File(filepath, out);
        case AudioFormat::OGG:  return decodeOggFile(filepath, out);
        default: return false;
    }
}

#ifdef _WIN32
bool AudioDecoder::decode(const wchar_t* filepath, PCMData& out) {
    switch (detectFormat(filepath)) {
        case AudioFormat::WAV:  return decodeWavFile(filepath, out);
        case AudioFormat::FLAC: return decodeFlacFile(filepath, out);
        case AudioFormat::MP3:  return decodeMp3File(filepath, out);
        case AudioFormat::OGG:  return decodeOggFile(filepath, out);
        default: return false;
    }
}
#endif

} // namespace loopfinder
