palace_smooth.dll is built on Windows (CI / FENRIR-STUDIO):

  cmake -S Native/smooth -B %TEMP%\palace-smooth-build -A x64 -DCMAKE_BUILD_TYPE=Release
  cmake --build %TEMP%\palace-smooth-build --config Release
  copy %TEMP%\palace-smooth-build\Release\palace_smooth.dll Native\smooth\win-x64\

Build outside the repo (or under Native/smooth/build, which is gitignored and
DefaultItemExcluded) so Diligent FetchContent .cs files are not compiled by
Palace.csproj. FetchContent pulls DiligentCore v2.5.6. See SOURCE.txt / THIRD_PARTY.md.
