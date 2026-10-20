# HDR PNG packaged verify (FenrirDesktop)

## Latest (tip df6dae3)

- Tip: `df6dae3da591a0f5dff7914c6c7a35cc718ca439`
- Details HDR: **presenting scRGB**
- Probe: `assumeHdr=1 displayHdr=1` · `swap=R16G16B16A16_FLOAT/scRGB(G10/P709)` · CCD `ACE=1 winHdr=1`

See `03-details-presenting-scrgb.png`, `04-details-color-hdr-scrgb.png`, `README-df6dae3.md`.

## Prior (tip 9fcceeed — tonemap regression baseline)

- Details HDR was `tonemap SDR 80 nits`
- Probe: `assumeHdr=0 displayHdr=0` (no swap=)
- Shots: `01-details-color-hdr.png`, `02-overlay-details-hdr.png`
