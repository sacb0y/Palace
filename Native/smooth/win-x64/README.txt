palace_smooth.dll is built on Windows (CI / FENRIR-STUDIO):

  cmake -S Native/smooth -B Native/smooth/build -A x64 -DCMAKE_BUILD_TYPE=Release
  cmake --build Native/smooth/build --config Release
  copy Native\smooth\build\Release\palace_smooth.dll Native\smooth\win-x64\

Native/smooth/build is gitignored and DefaultItemExcluded (with out\) so Diligent
FetchContent .cs are not compiled by Palace.csproj. Static Diligent link; runtime
HLSL via D3DCompile — no dxcompiler/dxil DLLs. See SOURCE.txt / THIRD_PARTY.md.
