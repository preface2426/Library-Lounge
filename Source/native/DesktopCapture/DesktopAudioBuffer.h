#pragma once

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>

class DesktopAudioBuffer {
public:
    static constexpr uint32_t SampleRate = 48000;

    void Clear() noexcept { first_ = count_ = 0; }

    void Push(const void* stereoPcm, uint32_t frames, uint64_t now) noexcept {
        Expire(now);
        const auto* pcm = static_cast<const unsigned char*>(stereoPcm);
        const uint32_t keep = std::min(frames, Capacity);
        constexpr uint32_t FrameBytes = 2 * sizeof(int16_t);
        if (pcm) pcm += static_cast<size_t>(frames - keep) * FrameBytes;

        const uint32_t drop = count_ + keep - std::min(count_ + keep, Capacity);
        first_ = (first_ + drop) % Capacity;
        count_ -= drop;
        uint32_t end = (first_ + count_) % Capacity;
        for (uint32_t i = 0; i < keep; ++i) {
            int16_t pair[2]{};
            if (pcm) std::memcpy(pair, pcm + i * FrameBytes, sizeof(pair));
            samples_[end] = (pair[0] + pair[1]) * (1.0f / 65536);
            receivedMs_[end] = now;
            if (++end == Capacity) end = 0;
        }
        count_ += keep;
    }

    uint32_t Read(float* output, uint32_t capacity, uint64_t now) noexcept {
        Expire(now);
        const uint32_t take = std::min(capacity, count_);
        if (!take) return 0;
        const uint32_t contiguous = std::min(take, Capacity - first_);
        std::copy_n(samples_.data() + first_, contiguous, output);
        std::copy_n(samples_.data(), take - contiguous, output + contiguous);
        first_ = (first_ + take) % Capacity;
        count_ -= take;
        return take;
    }

private:
    static constexpr uint32_t MaxBacklogMs = 100;
    static constexpr uint32_t Capacity = SampleRate * MaxBacklogMs / 1000;

    void Expire(uint64_t now) noexcept {
        while (count_ && now - receivedMs_[first_] >= MaxBacklogMs) {
            if (++first_ == Capacity) first_ = 0;
            --count_;
        }
    }

    std::array<float, Capacity> samples_{};
    std::array<uint64_t, Capacity> receivedMs_{};
    uint32_t first_ = 0;
    uint32_t count_ = 0;
};
