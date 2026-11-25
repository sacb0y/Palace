# Device-lost a6dcdcb — fence ERROR×3 ordering (Wimbly APPROVED tip)

## Wall-clock (America/New_York, harness)

| Event | Time (UTC-5) |
|---|---|
| ENV_PROBE NOTE=1 | 2026-11-25T13:18:44.376 |
| PHASE armed frame=785 6/6 | 2026-11-25T13:19:07.360 |
| First post-reset STATUS frame=3 6/6 | 2026-11-25T13:19:08.633 |

Gap armed→re-arm ≈ **1.27 s**. Diligent log lines have no wallclock; order within that window:

## Log line order (smooth-diligent-debug-devicelost.log)

1. **L123** RemoveDevice — InfoQueue DXGI_ERROR_DEVICE_REMOVED (PnP stop)
2. **L128** severity=ERROR GetCompletedFenceValue CommandQueueD3D12Impl.cpp:108
3. **L130** severity=ERROR (same assert #2)
4. **L132** severity=ERROR (same assert #3)
5. **L151** recreate — `D3D12 debug layer enabled before CreateDevice`
6. **L158** TryStart NOTE `PALACE_SMOOTH_TEST_REMOVE_DEVICE=1` again

**Confirmed:** all three fence ERRORs sit **strictly between** RemoveDevice (L123) and CreateDevice (L151). errorLines=3; no psv_* ERROR.

## Chain summary
armed@785 → RemoveDevice → fence ERROR×3 → CreateDevice → re-arm frame 3 6/6 → frames continue (late ~2888).
