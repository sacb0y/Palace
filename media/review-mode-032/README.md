# Review mode 0.3.2 screenshots (media-only)

Packaged **Debug** captures of Review mode on product tip `a7448c73e62b84554e5aa90625b55986c8f3cc73` (`cursor/review-mode-032-c7c7` / PR #129).

Hosted on this media branch — **not** on the product tip. Pin raw URLs to this media commit SHA.

Test identity: `PalaceReviewMode-129` (never C20A7629). Isolated package LocalState + temp SFW only. Captured on Main-Desktop (FenrirDesktop). BelowNormal, `-m:1`.

| File | Caption |
|------|---------|
| `01-a7448c7-review-chrome.png` | Review chrome: progress `1 of 5`; Reject (left), Skip (↑ / S), Keep (right), Undo. |
| `02-a7448c7-after-reject.png` | After Reject: progress advances; file moved to sibling `_rejected` (see `02b-rejected-folder-listing.txt`). |
| `03-a7448c7-skip-undecided.png` | After Skip: progress `N of 5 · 1 skipped`; file left in place (undecided). |
| `04-a7448c7-undo-skip.png` | Undo after Skip restores the skipped item and clears that skip from the count. |
| `05-a7448c7-review-n-skipped.png` | End of queue: `Done · 3 skipped` + CTA `Review 3 skipped`. |
