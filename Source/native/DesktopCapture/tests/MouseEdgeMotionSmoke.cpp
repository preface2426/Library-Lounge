#include "../MouseEdgeMotion.h"
#include <cassert>
#include <cstdio>

int main() {
    MouseEdgeMotion edge;
    // The report that reached the edge through Windows is never added again.
    assert(edge.Outward(2559,3,true,100)==0);
    assert(edge.Outward(2559,4,true,115)==0);
    // Continuous movement past the edge works even without any hook move.
    assert(edge.Outward(2559,5,true,125)==5);
    // The move replaying the exact edge is harmless. Re-entering the screen
    // or having Windows move that axis starts a new observation.
    edge.AnchorChanged(2559);
    assert(edge.Outward(2559,5,true,130)==5);
    edge.AnchorChanged(2558);
    assert(edge.Outward(2559,5,true,135)==0);
    edge.Reset();
    assert(edge.Outward(2559,5,true,165)==0);
    assert(edge.Outward(2559,-4,false,180)==0);
    assert(edge.Outward(-1920,-5,true,0xfffffff8u)==0);
    assert(edge.Outward(-1920,-6,true,17)==-6);
    assert(edge.Outward(-1920,2,false,18)==0);
    std::puts("MOUSE_EDGE_MOTION_PASS");
}
