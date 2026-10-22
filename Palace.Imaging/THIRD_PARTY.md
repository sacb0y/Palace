# Third-party notices — Palace.Imaging

## libplacebo (LGPL-2.1+)

SDR tonemap spline and `st2094_pick_knee`, plus the BT.2390 EETF helper, are
ported from [haasn/libplacebo](https://github.com/haasn/libplacebo)
`src/tone_mapping.c` at commit **`02d7e35e`**.

libplacebo is licensed under the GNU Lesser General Public License v2.1 or later.
A copy of the LGPL-2.1 text is available from the Free Software Foundation.
Palace links this math as a clean-room C# port inside a separate library; it does
not ship libplacebo binaries.

Do not copy additional libplacebo sources without updating this notice and
confirming license compatibility.
