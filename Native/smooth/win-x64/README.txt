palace_smooth.dll is built on Windows (CI / FENRIR-STUDIO):

  cmake -S Native/smooth -B Native/smooth/build -A x64 -DCMAKE_BUILD_TYPE=Release
  cmake --build Native/smooth/build --config Release
  copy Native\smooth\build\Release\palace_smooth.dll Native\smooth\win-x64\

FetchContent pulls DiligentCore v2.5.6. See SOURCE.txt / THIRD_PARTY.md.
