// palace_exr: a tiny C shim over OpenEXRCore for Palace.
// Decodes the first RGB(A) / Y image part of any scanline or tiled,
// single- or multi-part OpenEXR file (every compression OpenEXRCore
// supports: NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A, DWAA, DWAB,
// HTJ2K, ZSTD) from memory into caller-owned linear float RGBA.
// Deep parts and sub-sampled (RY/BY) channels are not decoded.
// SPDX-License-Identifier: BSD-3-Clause

#include <openexr.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#if defined(_WIN32)
#define PEXR_API __declspec(dllexport)
#else
#define PEXR_API __attribute__((visibility("default")))
#endif

enum
{
    PEXR_OK = 0,
    PEXR_ERR_OPEN = 1,
    PEXR_ERR_NO_IMAGE = 2,
    PEXR_ERR_SIZE = 3,
    PEXR_ERR_DECODE = 4,
    PEXR_ERR_ARGS = 5
};

enum
{
    PEXR_FLAG_TILED = 1,
    PEXR_FLAG_MULTIPART = 2,
    PEXR_FLAG_LUMINANCE = 4,
    PEXR_FLAG_HAS_ALPHA = 8
};

typedef struct
{
    const uint8_t* data;
    int64_t        size;
} pexr_mem;

static int64_t
mem_read (
    exr_const_context_t         ctxt,
    void*                       userdata,
    void*                       buffer,
    uint64_t                    sz,
    uint64_t                    offset,
    exr_stream_error_func_ptr_t error_cb)
{
    const pexr_mem* m = (const pexr_mem*) userdata;
    (void) ctxt;
    (void) error_cb;
    if (offset >= (uint64_t) m->size) return 0;
    uint64_t left = (uint64_t) m->size - offset;
    if (sz > left) sz = left;
    memcpy (buffer, m->data + offset, (size_t) sz);
    return (int64_t) sz;
}

static int64_t
mem_size (exr_const_context_t ctxt, void* userdata)
{
    (void) ctxt;
    return ((const pexr_mem*) userdata)->size;
}

static void
quiet_error (exr_const_context_t ctxt, exr_result_t code, const char* msg)
{
    (void) ctxt;
    (void) code;
    (void) msg;
}

/* Target slot (0=R 1=G 2=B 3=A) for a channel name, -1 when unused. */
typedef struct
{
    char prefix[256]; /* "" or "layer." */
    int  luminance;
    int  has_alpha;
} pexr_pick;

static int
ends_with_layer (const char* name, const char* prefix, const char* leaf)
{
    size_t pl = strlen (prefix);
    return strncmp (name, prefix, pl) == 0 && strcmp (name + pl, leaf) == 0;
}

static int
find_channel (const exr_attr_chlist_t* cl, const char* prefix, const char* leaf)
{
    for (int i = 0; i < cl->num_channels; ++i)
        if (ends_with_layer (cl->entries[i].name.str, prefix, leaf)) return i;
    return -1;
}

static int
channel_ok (const exr_attr_chlist_t* cl, int i)
{
    return i < 0 || (cl->entries[i].x_sampling == 1 && cl->entries[i].y_sampling == 1);
}

/* Pick the colour channels of a part: bare R/G/B[/A], else Y[/A], else
 * the first "layer.R/G/B" set. Returns 1 when the part is usable. */
static int
pick_channels (const exr_attr_chlist_t* cl, pexr_pick* pick)
{
    memset (pick, 0, sizeof (*pick));
    if (!cl || cl->num_channels <= 0) return 0;

    int r = find_channel (cl, "", "R"), g = find_channel (cl, "", "G"),
        b = find_channel (cl, "", "B");
    if (r >= 0 || g >= 0 || b >= 0)
    {
        if (!channel_ok (cl, r) || !channel_ok (cl, g) || !channel_ok (cl, b))
            return 0;
        pick->has_alpha = find_channel (cl, "", "A") >= 0;
        return 1;
    }

    int y = find_channel (cl, "", "Y");
    if (y >= 0)
    {
        if (!channel_ok (cl, y)) return 0;
        pick->luminance = 1;
        pick->has_alpha = find_channel (cl, "", "A") >= 0;
        return 1;
    }

    for (int i = 0; i < cl->num_channels; ++i)
    {
        const char* n   = cl->entries[i].name.str;
        const char* dot = strrchr (n, '.');
        if (!dot || strcmp (dot + 1, "R") != 0) continue;
        size_t pl = (size_t) (dot - n) + 1;
        if (pl >= sizeof (pick->prefix)) continue;
        memcpy (pick->prefix, n, pl);
        pick->prefix[pl] = '\0';
        if (find_channel (cl, pick->prefix, "G") >= 0 &&
            find_channel (cl, pick->prefix, "B") >= 0 &&
            channel_ok (cl, i) &&
            channel_ok (cl, find_channel (cl, pick->prefix, "G")) &&
            channel_ok (cl, find_channel (cl, pick->prefix, "B")))
        {
            pick->has_alpha = find_channel (cl, pick->prefix, "A") >= 0;
            return 1;
        }
    }
    pick->prefix[0] = '\0';
    return 0;
}

static int
slot_for (const char* name, const pexr_pick* pick)
{
    if (pick->luminance)
    {
        if (strcmp (name, "Y") == 0) return 0;
        if (strcmp (name, "A") == 0) return 3;
        return -1;
    }
    if (ends_with_layer (name, pick->prefix, "R")) return 0;
    if (ends_with_layer (name, pick->prefix, "G")) return 1;
    if (ends_with_layer (name, pick->prefix, "B")) return 2;
    if (ends_with_layer (name, pick->prefix, "A")) return 3;
    return -1;
}

typedef struct
{
    exr_context_t ctx;
    int           part;
    int           tiled;
    int           multipart;
    int32_t       x0, y0, w, h;
    pexr_pick     pick;
    exr_compression_t compression;
} pexr_file;

static int
open_file (const uint8_t* data, int64_t size, pexr_file* f)
{
    memset (f, 0, sizeof (*f));
    f->part = -1;
    if (!data || size < 8) return PEXR_ERR_ARGS;

    return PEXR_OK;
}

static int
start (pexr_mem* mem, pexr_file* f)
{
    exr_context_initializer_t init = EXR_DEFAULT_CONTEXT_INITIALIZER;
    init.user_data        = mem;
    init.read_fn          = &mem_read;
    init.size_fn          = &mem_size;
    init.error_handler_fn = &quiet_error;
    if (exr_start_read (&f->ctx, "<memory>", &init) != EXR_ERR_SUCCESS)
    {
        f->ctx = NULL;
        return PEXR_ERR_OPEN;
    }

    int count = 0;
    if (exr_get_count (f->ctx, &count) != EXR_ERR_SUCCESS || count <= 0)
        return PEXR_ERR_NO_IMAGE;
    f->multipart = count > 1;

    for (int p = 0; p < count; ++p)
    {
        exr_storage_t st;
        if (exr_get_storage (f->ctx, p, &st) != EXR_ERR_SUCCESS) continue;
        if (st != EXR_STORAGE_SCANLINE && st != EXR_STORAGE_TILED) continue;
        const exr_attr_chlist_t* cl = NULL;
        if (exr_get_channels (f->ctx, p, &cl) != EXR_ERR_SUCCESS) continue;
        pexr_pick pick;
        if (!pick_channels (cl, &pick)) continue;
        exr_attr_box2i_t dw;
        if (exr_get_data_window (f->ctx, p, &dw) != EXR_ERR_SUCCESS) continue;
        int64_t w = (int64_t) dw.max.x - dw.min.x + 1;
        int64_t h = (int64_t) dw.max.y - dw.min.y + 1;
        if (w <= 0 || h <= 0 || w > 65536 || h > 65536) continue;
        exr_compression_t comp;
        if (exr_get_compression (f->ctx, p, &comp) != EXR_ERR_SUCCESS ||
            (int) comp < 0 || comp >= EXR_COMPRESSION_LAST_TYPE)
            continue; /* unknown method: nothing can decode it */
        f->part  = p;
        f->tiled = st == EXR_STORAGE_TILED;
        f->x0    = dw.min.x;
        f->y0    = dw.min.y;
        f->w     = (int32_t) w;
        f->h     = (int32_t) h;
        f->pick  = pick;
        f->compression = comp;
        return PEXR_OK;
    }
    return PEXR_ERR_NO_IMAGE;
}

/* px / py: absolute pixel origin of the chunk (scanline chunks carry it in
 * start_x / start_y; tile chunks carry tile indices there instead). */
static int
decode_chunk (
    pexr_file* f, exr_chunk_info_t* ci, int64_t px, int64_t py, float* dst,
    int64_t dst_floats)
{
    exr_decode_pipeline_t dec = EXR_DECODE_PIPELINE_INITIALIZER;
    int                   rv  = PEXR_OK;
    if (exr_decoding_initialize (f->ctx, f->part, ci, &dec) != EXR_ERR_SUCCESS)
        return PEXR_ERR_DECODE;

    int64_t row_floats = (int64_t) f->w * 4;
    int64_t cx         = px - f->x0;
    int64_t cy         = py - f->y0;
    if (cx < 0 || cy < 0 || cx + ci->width > f->w || cy + ci->height > f->h)
    {
        exr_decoding_destroy (f->ctx, &dec);
        return PEXR_ERR_DECODE;
    }

    int any = 0;
    for (int c = 0; c < dec.channel_count; ++c)
    {
        exr_coding_channel_info_t* ch   = &dec.channels[c];
        int                        slot = slot_for (ch->channel_name, &f->pick);
        if (slot < 0 || ch->x_samples != 1 || ch->y_samples != 1)
        {
            ch->decode_to_ptr = NULL;
            continue;
        }
        int64_t off = (cy * row_floats) + (cx * 4) + slot;
        if (off + ((int64_t) (ci->height - 1) * row_floats) +
                ((int64_t) (ci->width - 1) * 4) >= dst_floats)
        {
            rv = PEXR_ERR_DECODE;
            break;
        }
        ch->decode_to_ptr          = (uint8_t*) (dst + off);
        ch->user_pixel_stride      = 4 * (int32_t) sizeof (float);
        ch->user_line_stride       = (int32_t) (row_floats * sizeof (float));
        ch->user_bytes_per_element = (int16_t) sizeof (float);
        ch->user_data_type         = EXR_PIXEL_FLOAT;
        any                        = 1;
    }

    if (rv == PEXR_OK && any)
    {
        if (exr_decoding_choose_default_routines (f->ctx, f->part, &dec) !=
                EXR_ERR_SUCCESS ||
            exr_decoding_run (f->ctx, f->part, &dec) != EXR_ERR_SUCCESS)
            rv = PEXR_ERR_DECODE;
    }
    exr_decoding_destroy (f->ctx, &dec);
    return rv;
}

/** Version string of the bundled OpenEXR. */
PEXR_API const char*
pexr_version (void)
{
    static char buf[64];
    int         maj = 0, min = 0, pat = 0;
    const char* extra = NULL;
    exr_get_library_version (&maj, &min, &pat, &extra);
    snprintf (buf, sizeof (buf), "OpenEXR %d.%d.%d", maj, min, pat);
    return buf;
}

/** Header only: size of the part pexr_decode_rgba would decode. */
PEXR_API int
pexr_info (
    const uint8_t* data,
    int64_t        size,
    int32_t*       width,
    int32_t*       height,
    int32_t*       flags,
    int32_t*       compression)
{
    pexr_file f;
    int       rv = open_file (data, size, &f);
    if (rv != PEXR_OK) return rv;
    pexr_mem mem = { data, size };
    rv           = start (&mem, &f);
    if (rv == PEXR_OK)
    {
        if (width) *width = f.w;
        if (height) *height = f.h;
        if (flags)
            *flags = (f.tiled ? PEXR_FLAG_TILED : 0) |
                     (f.multipart ? PEXR_FLAG_MULTIPART : 0) |
                     (f.pick.luminance ? PEXR_FLAG_LUMINANCE : 0) |
                     (f.pick.has_alpha ? PEXR_FLAG_HAS_ALPHA : 0);
        if (compression) *compression = (int32_t) f.compression;
    }
    if (f.ctx) exr_finish (&f.ctx);
    return rv;
}

/** Decode into dst (width*height*4 floats, RGBA, row-major, top-down).
 * Missing G/B are 0, missing A is 1, Y fills R=G=B. */
PEXR_API int
pexr_decode_rgba (
    const uint8_t* data, int64_t size, float* dst, int64_t dst_floats)
{
    pexr_file f;
    int       rv = open_file (data, size, &f);
    if (rv != PEXR_OK) return rv;
    if (!dst) return PEXR_ERR_ARGS;
    pexr_mem mem = { data, size };
    rv           = start (&mem, &f);
    if (rv != PEXR_OK)
    {
        if (f.ctx) exr_finish (&f.ctx);
        return rv;
    }

    int64_t total = (int64_t) f.w * f.h * 4;
    if (dst_floats < total)
    {
        exr_finish (&f.ctx);
        return PEXR_ERR_SIZE;
    }
    for (int64_t i = 0; i < total; i += 4)
    {
        dst[i] = dst[i + 1] = dst[i + 2] = 0.f;
        dst[i + 3]                       = 1.f;
    }

    if (f.tiled)
    {
        int32_t  tx = 0, ty = 0;
        uint32_t tw = 0, th = 0;
        if (exr_get_tile_counts (f.ctx, f.part, 0, 0, &tx, &ty) != EXR_ERR_SUCCESS ||
            exr_get_tile_descriptor (f.ctx, f.part, &tw, &th, NULL, NULL) !=
                EXR_ERR_SUCCESS ||
            tw == 0 || th == 0)
            rv = PEXR_ERR_DECODE;
        for (int32_t y = 0; rv == PEXR_OK && y < ty; ++y)
            for (int32_t x = 0; rv == PEXR_OK && x < tx; ++x)
            {
                exr_chunk_info_t ci;
                if (exr_read_tile_chunk_info (f.ctx, f.part, x, y, 0, 0, &ci) !=
                    EXR_ERR_SUCCESS)
                    rv = PEXR_ERR_DECODE;
                else
                    rv = decode_chunk (
                        &f, &ci, (int64_t) f.x0 + (int64_t) x * tw,
                        (int64_t) f.y0 + (int64_t) y * th, dst, dst_floats);
            }
    }
    else
    {
        int32_t lines = 1;
        if (exr_get_scanlines_per_chunk (f.ctx, f.part, &lines) != EXR_ERR_SUCCESS ||
            lines <= 0)
            rv = PEXR_ERR_DECODE;
        for (int64_t y = f.y0; rv == PEXR_OK && y < (int64_t) f.y0 + f.h; y += lines)
        {
            exr_chunk_info_t ci;
            if (exr_read_scanline_chunk_info (f.ctx, f.part, (int) y, &ci) !=
                EXR_ERR_SUCCESS)
                rv = PEXR_ERR_DECODE;
            else
                rv = decode_chunk (
                    &f, &ci, ci.start_x, ci.start_y, dst, dst_floats);
            /* Chunks are aligned to the data window; step from their start. */
            if (rv == PEXR_OK) y = (int64_t) ci.start_y + ci.height - lines;
        }
    }

    if (rv == PEXR_OK && f.pick.luminance)
        for (int64_t i = 0; i < total; i += 4)
            dst[i + 1] = dst[i + 2] = dst[i];

    exr_finish (&f.ctx);
    return rv;
}
