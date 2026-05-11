#include <jni.h>
#include <string>
#include <vector>

#include "loopfinder/common.h"
#include "loopfinder/audio_decoder.h"
#include "loopfinder/loop_finder.h"

extern "C" {

JNIEXPORT jobjectArray JNICALL
Java_com_cpu_seamlessloopmobile_jni_NativeAudio_analyzeLoopPoints(
    JNIEnv* env, jclass /*clazz*/, jstring filePath, jint topN)
{
    // 1. Get file path from jstring
    const char* pathCStr = env->GetStringUTFChars(filePath, nullptr);
    if (!pathCStr) return nullptr;
    std::string path(pathCStr);
    env->ReleaseStringUTFChars(filePath, pathCStr);

    // 2. Decode audio
    loopfinder::PCMData pcm;
    loopfinder::AudioDecoder decoder;
    if (!decoder.decode(path.c_str(), pcm)) {
        return nullptr;
    }

    // 3. Configure and run loop finder
    loopfinder::LoopFinder::Config config;
    config.minDurationMultiplier = 0.35f;
    config.topN = std::max(1, static_cast<int>(topN));

    auto loopPoints = loopfinder::LoopFinder().analyze(
        pcm.samples.data(),
        static_cast<int>(pcm.samples.size()),
        pcm.sampleRate,
        config);

    // 4. Apply trim offset so loop points are relative to original file
    for (auto& lp : loopPoints) {
        lp.loopStart += pcm.trimStart;
        lp.loopEnd   += pcm.trimStart;
    }

    // 5. Find LoopPoint Java class and constructor
    jclass loopPointClass = env->FindClass("com/cpu/seamlessloopmobile/jni/LoopPoint");
    if (!loopPointClass) return nullptr;

    jmethodID constructor = env->GetMethodID(loopPointClass, "<init>", "(JJFFF)V");
    if (!constructor) return nullptr;

    // 6. Create Java array of LoopPoint
    int count = static_cast<int>(loopPoints.size());
    jobjectArray result = env->NewObjectArray(count, loopPointClass, nullptr);
    if (!result) return nullptr;

    for (int i = 0; i < count; ++i) {
        jobject lpObj = env->NewObject(
            loopPointClass, constructor,
            static_cast<jlong>(loopPoints[i].loopStart),
            static_cast<jlong>(loopPoints[i].loopEnd),
            static_cast<jfloat>(loopPoints[i].noteDiff),
            static_cast<jfloat>(loopPoints[i].loudnessDiff),
            static_cast<jfloat>(loopPoints[i].score));
        env->SetObjectArrayElement(result, i, lpObj);
        env->DeleteLocalRef(lpObj);
    }

    return result;
}

} // extern "C"
