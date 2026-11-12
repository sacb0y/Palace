cmake_minimum_required(VERSION 3.24)
project(palace_mesh3d LANGUAGES CXX)

if(NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "palace_mesh3d is x64-only for this spike")
endif()

option(PALACE_MESH3D_WITH_DILIGENT "Link DiligentCore/Tools/FX for glTF PBR+IBL" OFF)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(palace_mesh3d SHARED
  src/palace_mesh3d.cpp
)
target_include_directories(palace_mesh3d PUBLIC include)
target_compile_definitions(palace_mesh3d PRIVATE PALACE_MESH3D_EXPORTS UNICODE _UNICODE)
target_link_libraries(palace_mesh3d PRIVATE d3d12 dxgi)

if(PALACE_MESH3D_WITH_DILIGENT)
  if(NOT DEFINED DILIGENT_ROOT)
    message(FATAL_ERROR "Set -DDILIGENT_ROOT=... to Diligent parent (Core/Tools/FX clones)")
  endif()
  target_compile_definitions(palace_mesh3d PRIVATE PALACE_MESH3D_WITH_DILIGENT=1)
  # Callers extend this target with Diligent include dirs + libs after Fetch/add_subdirectory.
  message(STATUS "PALACE_MESH3D_WITH_DILIGENT=ON — wire Diligent includes/libs in tools/mesh3d/build.ps1")
endif()

install(TARGETS palace_mesh3d RUNTIME DESTINATION bin)
