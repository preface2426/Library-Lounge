Gobkit Free Animal Pack — 10 rigged & animated low-poly creatures (CC0)
=======================================================================

10 creatures, each a separate .glb, sharing one humanoid-ish skeleton and
the same 4 baked animation clips:

  Anglerfish, Shark, Platypus, Rhino, Duck, Bat, Corgi, Jellyfish, Hippo, Red

ANIMATION (single track per file, 120 frames @ 24 fps — subclip these ranges):
  idle    frames  0–29
  attack  frames 30–59
  dead    frames 60–89
  walk    frames 90–119

Each .glb has its texture atlas embedded. The raw atlas (Texture001.png) is
included too, in case you want to recolor.

QUICK START (Three.js):
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { AnimationUtils, AnimationMixer } from 'three';
  new GLTFLoader().load('Corgi.glb', (g) => {
    scene.add(g.scene);
    const mixer = new AnimationMixer(g.scene);
    const walk = AnimationUtils.subclip(g.animations[0], 'walk', 90, 119, 24);
    mixer.clipAction(walk).play();
  });

LICENSE: CC0 1.0 (public domain) — free for any use, commercial or personal,
no attribution required.

More free characters + a one-call generation API: https://gobkit.com/freebies
