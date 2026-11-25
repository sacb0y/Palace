cmake_minimum_required(VERSION 3.24)

# Static CRT for palace_smooth + Diligent static libs (no MSVCP140 / VCRUNTIME140 DLLs).
if(POLICY CMP0091)
  cmake_policy(SET CMP0091 NEW)
endif()
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project(palace_smooth LANGUAGES CXX)

if(NOT WIN32)
  message(FATAL_ERROR "palace_smooth is Windows-only (D3D12/D3D11 + SwapChainPanel)")
endif()

# x64-only for every generator: pointer width + VS platform + obvious non-x64 CPUs.
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "palace_smooth is x64-only (CMAKE_SIZEOF_VOID_P=${CMAKE_SIZEOF_VOID_P})")
endif()

if(DEFINED CMAKE_GENERATOR_PLATFORM AND NOT "${CMAKE_GENERATOR_PLATFORM}" STREQUAL "")
  string(TOUPPER "${CMAKE_GENERATOR_PLATFORM}" _palace_smooth_gen_plat)
  if(NOT _palace_smooth_gen_plat STREQUAL "X64" AND NOT _palace_smooth_gen_plat STREQUAL "AMD64")
    message(FATAL_ERROR "palace_smooth rejects non-x64 generator platform '${CMAKE_GENERATOR_PLATFORM}'")
  endif()
endif()

if(DEFINED CMAKE_VS_PLATFORM_NAME AND NOT "${CMAKE_VS_PLATFORM_NAME}" STREQUAL "")
  string(TOUPPER "${CMAKE_VS_PLATFORM_NAME}" _palace_smooth_vs_plat)
  if(NOT _palace_smooth_vs_plat STREQUAL "X64" AND NOT _palace_smooth_vs_plat STREQUAL "AMD64")
    message(FATAL_ERROR "palace_smooth rejects non-x64 VS platform '${CMAKE_VS_PLATFORM_NAME}'")
  endif()
endif()

if(CMAKE_SYSTEM_PROCESSOR)
  string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" _palace_smooth_proc)
  if(_palace_smooth_proc MATCHES "^(arm|arm64|aarch64)$"
      OR _palace_smooth_proc MATCHES "^i[3-6]86$"
      OR _palace_smooth_proc STREQUAL "x86")
    message(FATAL_ERROR "palace_smooth rejects non-x64 CMAKE_SYSTEM_PROCESSOR '${CMAKE_SYSTEM_PROCESSOR}'")
  endif()
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(PALACE_SMOOTH_WITH_DILIGENT "Fetch and link DiligentCore (required for product builds)" ON)
set(PALACE_SMOOTH_DILIGENT_TAG "v2.5.6" CACHE STRING "DiligentCore git tag")

add_library(palace_smooth SHARED
  src/palace_smooth.cpp
  src/palace_smooth_tiles.cpp
)
set_property(TARGET palace_smooth PROPERTY
  MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
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
  # V0 needs D3D11/D3D12 + HLSL only — cut GL/Vk/Metal/WebGPU/Archiver (~10 min cold).
  set(DILIGENT_NO_GLSLANG ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_HLSL OFF CACHE BOOL "" FORCE)
  set(DILIGENT_NO_OPENGL ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_VULKAN ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_METAL ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_WEBGPU ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_ARCHIVER ON CACHE BOOL "" FORCE)
  set(DILIGENT_NO_SUPER_RESOLUTION ON CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_SAMPLES OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_FX OFF CACHE BOOL "" FORCE)
  set(DILIGENT_BUILD_TESTS OFF CACHE BOOL "" FORCE)
  set(DILIGENT_INSTALL_CORE OFF CACHE BOOL "" FORCE)
  # Inherit static CRT into FetchContent Diligent targets.
  set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>" CACHE STRING "" FORCE)

  FetchContent_Declare(
    DiligentCore
    GIT_REPOSITORY https://github.com/DiligentGraphics/DiligentCore.git
    GIT_TAG ${PALACE_SMOOTH_DILIGENT_TAG}
    GIT_SHALLOW TRUE
  )
  FetchContent_MakeAvailable(DiligentCore)

  # Diligent still declares *-shared DLL targets; exclude them (static-only V0).
  foreach(_palace_smooth_shared
      Diligent-GraphicsEngineD3D11-shared
      Diligent-GraphicsEngineD3D12-shared
      Diligent-GraphicsEngineOpenGL-shared
      Diligent-GraphicsEngineVk-shared
      Diligent-GraphicsEngineWebGPU-shared
      Diligent-Archiver-shared)
    if(TARGET ${_palace_smooth_shared})
      set_target_properties(${_palace_smooth_shared} PROPERTIES EXCLUDE_FROM_ALL TRUE)
    endif()
  endforeach()

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
  message(STATUS "palace_smooth: DiligentCore ${PALACE_SMOOTH_DILIGENT_TAG} (static CRT, D3D11/D3D12 only)")
else()
  message(WARNING "palace_smooth: Diligent OFF — stub D3D12/D3D11 clear only (dev)")
endif()

install(TARGETS palace_smooth RUNTIME DESTINATION bin)
