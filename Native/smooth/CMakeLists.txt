cmake_minimum_required(VERSION 3.24)
project(palace_smooth LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "palace_smooth is Windows-only (D3D12/D3D11 + SwapChainPanel)")
endif()

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "palace_smooth is x64-only (CMAKE_SIZEOF_VOID_P=${CMAKE_SIZEOF_VOID_P})")
endif()

# VS -A Win32 / arm64 must not silently produce a non-x64 DLL.
if(DEFINED CMAKE_GENERATOR_PLATFORM AND NOT "${CMAKE_GENERATOR_PLATFORM}" STREQUAL "")
  string(TOUPPER "${CMAKE_GENERATOR_PLATFORM}" _palace_smooth_plat)
  if(NOT _palace_smooth_plat STREQUAL "X64" AND NOT _palace_smooth_plat STREQUAL "AMD64")
    message(FATAL_ERROR "palace_smooth rejects non-x64 generator platform '${CMAKE_GENERATOR_PLATFORM}'")
  endif()
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(PALACE_SMOOTH_WITH_DILIGENT "Fetch and link DiligentCore (required for product builds)" ON)
set(PALACE_SMOOTH_DILIGENT_TAG "v2.5.6" CACHE STRING "DiligentCore git tag")

add_library(palace_smooth SHARED
  src/palace_smooth.cpp
)
target_include_directories(palace_smooth PUBLIC include)
target_compile_definitions(palace_smooth PRIVATE
  PALACE_SMOOTH_EXPORTS
  UNICODE
  _UNICODE
  NOMINMAX
  WIN32_LEAN_AND_MEAN
)
target_link_libraries(palace_smooth PRIVATE d3d12 d3d11 dxgi d3dcompiler)

if(PALACE_SMOOTH_WITH_DILIGENT)
  include(FetchContent)
  set(DILIGENT_NO_GLSLANG ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_HLSL OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_SAMPLES OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_FX OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_TESTS OFF CACHE BOOL "" FORCE)
  set(DILIGENT_INSTALL_CORE OFF CACHE BOOL "" FORCE)
  FetchContent_Declare(
    DiligentCore
    GIT_REPOSITORY https://github.com/DiligentGraphics/DiligentCore.git
    GIT_TAG ${PALACE_SMOOTH_DILIGENT_TAG}
    GIT_SHALLOW TRUE
  )
  FetchContent_MakeAvailable(DiligentCore)

  target_compile_definitions(palace_smooth PRIVATE PALACE_SMOOTH_WITH_DILIGENT=1)
  # GraphicsEngineD3D*-static link Diligent-Common as PRIVATE, so Common/Primitives
  # interface headers (RefCntAutoPtr.hpp, etc.) do not propagate. Link them here.
  target_link_libraries(palace_smooth PRIVATE
    Diligent-BuildSettings
    Diligent-Common
    Diligent-Primitives
    Diligent-GraphicsEngineD3D12-static
    Diligent-GraphicsEngineD3D11-static
  )
  message(STATUS "palace_smooth: DiligentCore ${PALACE_SMOOTH_DILIGENT_TAG}")
else()
  message(WARNING "palace_smooth: Diligent OFF — stub D3D12/D3D11 clear only (dev)")
endif()

install(TARGETS palace_smooth RUNTIME DESTINATION bin)
