# PR #118 Details hit-test screenshots (0.2.5)

- Product tip: `74abeb106240c9a8e25c74fa05285b53b57f84d0` (`cursor/details-025-hittest-2d37`)
- Identity: `PalaceDetailsHit-025` (build AppxManifest only; never C20A7629)
- Media-only branch `cursor/details-025-media-2d37`
- Temp SFW under package LocalState; catalog isolated

## Coverage

Hover + click proofs on overlay and GalleryWindow for footer/actions.
Section expanders File / Histogram / Gamut / Tags (collapse + expand) on both hosts.
Next preserves collapsed Tags on both hosts.

## Notes

- Color and HDR expander not shown: InfoHasColorSection stayed false for these SFW JPEGs/PNGs.
- Histogram Compact=True hides Luma/RGB mode toggles in Details card.
