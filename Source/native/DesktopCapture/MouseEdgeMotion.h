#pragma once
#include <cstdint>

// Only extend a raw axis after repeated outward reports at the same Windows
// screen edge. A normal Windows move or an anchor change restarts the check.
struct MouseEdgeMotion {
    static constexpr std::uint32_t holdMs = 24;

    bool waiting = false;
    long anchor = 0;
    bool negative = false;
    std::uint32_t since = 0;

    constexpr void Reset() { waiting=false; }
    constexpr void AnchorChanged(long position) { if(waiting&&anchor!=position)Reset(); }
    constexpr long Outward(long position,long delta,bool blocked,std::uint32_t time) {
        if(!delta)return 0;
        if(!blocked){Reset();return 0;}
        if(!waiting||anchor!=position||negative!=(delta<0)){
            waiting=true;anchor=position;negative=delta<0;since=time;
            return 0;
        }
        const auto elapsed=time-since;
        return elapsed>=holdMs&&elapsed<0x80000000u?delta:0;
    }
};
