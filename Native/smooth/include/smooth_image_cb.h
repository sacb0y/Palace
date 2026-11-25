/* C mirrors of the HLSL constant / structured buffers (16-byte aligned). */
#pragma once
#include <stdint.h>

typedef struct psv_view_cb {
    float img_to_dev0[4];  /* a, b, tx, 0  : dev.x = a*img.x + b*img.y + tx */
    float img_to_dev1[4];  /* c, d, ty, 0  : dev.y = c*img.x + d*img.y + ty */
    float dev_to_img0[4];  /* inverse affine, same layout */
    float dev_to_img1[4];
    float viewport[4];     /* 2/W, 2/H, zoom, log2(1/zoom) */
    float filter[4];       /* nearest_start=4, nearest_full=6, aa_nearest=1, 0 */
    float grid[4];         /* grid_opacity (toggle*ease 0..1), 8, 10, 0 */
    float tile[4];         /* slot=512, gutter=2, 1/512, 0 */
} psv_view_cb;             /* 128 bytes */

typedef struct psv_tone_cb {
    float tone_a[4];       /* Lc, P50, PeakNormalized, Ld */
    float tone_b[4];       /* ExposureEv, 2^EV, UseHdrPath, OutputLimit (HDR: Ld, SDR: PeakNormalized) */
    float lut[4];          /* InputMinPq, InputMaxPq, LutSize, HasLut */
    float present[4];      /* OutputScale, ClipScrgb, SoftNegatives, PaperScrgb */
} psv_tone_cb;             /* 64 bytes */

typedef struct psv_tile_instance {   /* StructuredBuffer stride 48 */
    float img_origin[2];
    float img_size[2];
    float parent_origin[2];
    float level_scale;     /* 2^level */
    float level;
    uint32_t slice;
    uint32_t parent_slice; /* 0xFFFFFFFF = none */
    float opacity;
    float pad;
} psv_tile_instance;

typedef struct psv_downsample_cb {
    uint32_t src_rect[4];
    uint32_t dst_rect[4];
} psv_downsample_cb;
