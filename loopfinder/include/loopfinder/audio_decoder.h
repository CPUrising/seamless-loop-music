#pragma once

#include "loopfinder/common.h"

namespace loopfinder {

class AudioDecoder {
public:
    AudioDecoder() = default;
    ~AudioDecoder();

    bool decode(const char* filepath, PCMData& out);

#ifdef _WIN32
    // 宽字符路径入口：系统 ANSI 代码页无法表示非 ASCII 路径时仍能打开文件
    bool decode(const wchar_t* filepath, PCMData& out);
#endif
};

} // namespace loopfinder
