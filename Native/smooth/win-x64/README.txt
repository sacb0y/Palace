palace_smooth.dll is built on Windows (CI / FENRIR-STUDIO):

  cmake -S Native/smooth -B Native/smooth/build -A x64 -DCMAKE_BUILD_TYPE=Release
  cmake --build Native/smooth/build --config Release --target palace_smooth
  copy Native\smooth\build\Release\palace_smooth.dll Native\smooth\win-x64\

Native/smooth/build is gitignored and DefaultItemExcluded (with out\) so Diligent
FetchContent .cs are not compiled by Palace.csproj. Static Diligent + static CRT
(/MT); runtime HLSL via D3DCompile — no dxcompiler/dxil/MSVCP140/VCRUNTIME140.
Build with: cmake --build Native/smooth/build --config Release --target palace_smooth
See SOURCE.txt / THIRD_PARTY.md.
