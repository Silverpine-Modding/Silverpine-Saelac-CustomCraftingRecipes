# Station art direction

These are original, transparent sprites made with built-in image generation. Only the three new sprites ship. No native reference artwork is included in the game package.

The initial workbench reference was an atlas, not the front frame by itself. The second pass used an enlarged local sheet of sprite rectangles (native sprite metadata, including bottom-origin coordinates), not the combined textures. References: workbench_0, table_wood, table_alchemy_0, chair_front, bookshelf, divider_0, stove_0, oven_1 and dresser_0. Key comparisons: broad visible tabletops; horizontal, parallel rails; oven crown viewed from above, with the mouth low on the near edge. The room divider has an angled silhouette of its own, not a universal diagonal camera.

Import: crop transparent export padding, preserve aspect and alpha, area-average to a 32×24, 32×32 or 44×42 canvas respectively. Custom Item Loader uses point sampling, centered pivots and 32 pixels/unit. Placement scale is 1. Front views only: no unverified alternate rotations.

## Final generation prompts

Shared reference prompt:

Use case: stylized-concept. Return ONE isolated game-world furniture sprite in genuine transparency. Image 1 is a contact sheet of original Silverpine gameplay frames at identical 4x nearest-neighbor scale: reference only, never reproduce the sheet, labels or backdrop. The actual game has a steep elevated, cardinal orthographic view: horizontal edges perfectly horizontal, vertical plan-depth edges go straight up the screen, no diagonal vanishing lines. Top planes are large. Softly mottled muted low contrast pre-rendered early RPG pixels, no drawn outlines, no strong rim highlights, no studio light, no fantasy icon style. Read as a small prop with tiny clusters at native size, not illustrative pixel art. Center, no detached shadows, neutral light.

joiner: Make a low Joiner's Bench. Use the TOP LEFT workbench as a strict silhouette template and the TOP MIDDLE plank table for top surface. Put a shallow vise at near edge, short hand plane and boards lying flat on top. Front legs small/thin. Width 32 x height 24 native pixels. Camera must look down into the wide flat top; straight edges, no visible side, absolutely no perspective convergence.

tailor: Make a Tailor's Frame with a muted grey-blue length of cloth, a spool and comb. A low angled warp frame over a table, seen significantly FROM ABOVE so the cloth is a broad top-facing rectangle and only about a quarter of the height shows feet below. Match the TOP MIDDLE wooden table and MIDDLE LEFT chair for axis and overhead depth, not an upright easel photographed at eye level. 32x32 native pixels.

oven: Make a simple village bake oven, use BOTTOM MIDDLE stone oven as a strict camera silhouette template. Nearly round, broad gray-brown crown and short front apron. Dark opening is only a small slit at the very bottom, no tall arch. Very low base, no tall stand. A few brick joints, dull clay at the lip. Top is more than two thirds of the silhouette; no dark side elevation. 44x42 native pixels.
