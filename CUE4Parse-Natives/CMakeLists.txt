cmake_minimum_required(VERSION 3.10)
project(CUE4Parse-Natives CXX)

set(CMAKE_CXX_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Add compiler flags for Clang
if(CMAKE_CXX_COMPILER_ID MATCHES "Clang")
    add_compile_options(
        -Wno-missing-template-arg-list-after-template-kw
        -Wno-error=missing-template-arg-list-after-template-kw
    )
endif()

# Suppress packed-member alignment warning in Oodle (GCC 13+)
if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
    add_compile_options(-Wno-address-of-packed-member)
endif()

if (EXISTS "${PROJECT_SOURCE_DIR}/ACL/external/acl/includes")
    set(WITH_ACL 1)
endif()

if (EXISTS "${PROJECT_SOURCE_DIR}/Oodle/external/oodle2/core")
    set(WITH_Oodle 1)
endif()

if (EXISTS "${PROJECT_SOURCE_DIR}/VgmStream/external/vgmstream/src")
    set(WITH_VgmStream 1)
endif()

# ACL
if (WITH_ACL)
        add_compile_definitions(WITH_ACL)
        include_directories("${PROJECT_SOURCE_DIR}/ACL/external/acl/includes")
        include_directories("${PROJECT_SOURCE_DIR}/ACL/external/acl/external/rtm/includes")
        set(SOURCES ${SOURCES} ${PROJECT_SOURCE_DIR}/ACL/ACL.cpp)
endif()

# Oodle
if (WITH_Oodle)
        add_compile_definitions(OODLE_BUILDING_DLL)
        add_compile_definitions(WITH_Oodle)
        add_subdirectory("${PROJECT_SOURCE_DIR}/Oodle/")
        target_compile_options(oodle2 PRIVATE -w)  # suppress all warnings
endif()

# VgmStream
if (WITH_VgmStream)
        add_compile_definitions(WITH_VgmStream)
        include_directories("${PROJECT_SOURCE_DIR}/VgmStream/external/vgmstream/src")
        include_directories("${PROJECT_SOURCE_DIR}/VgmStream/includes")
        set(SOURCES ${SOURCES} ${PROJECT_SOURCE_DIR}/VgmStream/VgmStream.cpp)
        add_subdirectory("${PROJECT_SOURCE_DIR}/VgmStream")
endif()

set(SOURCES ${SOURCES} ${PROJECT_SOURCE_DIR}/Features.cpp)
include_directories("${PROJECT_SOURCE_DIR}/common")

add_library("${PROJECT_NAME}" SHARED ${SOURCES})
set_target_properties("${PROJECT_NAME}" PROPERTIES PREFIX "")

# Oodle
if (WITH_Oodle)
    target_link_libraries("${PROJECT_NAME}" PRIVATE oodle2)

    # Ensure all Oodle symbols are exported with the shared library
    if(WIN32)
        target_link_options("${PROJECT_NAME}" PRIVATE "/WHOLEARCHIVE:oodle2")
    elseif(APPLE)
        target_link_options("${PROJECT_NAME}" PRIVATE "-Wl,-force_load,$<TARGET_FILE:oodle2>")
    else() # Linux and other platforms
        target_link_options("${PROJECT_NAME}" PRIVATE "-Wl,--whole-archive" "$<TARGET_FILE:oodle2>" "-Wl,--no-whole-archive")
    endif()
endif()

# VgmStream
if (WITH_VgmStream)
    target_link_libraries("${PROJECT_NAME}" PRIVATE libvgmstream)

    # Link vgmstream dependencies
    if (NOT WIN32)
        # Link math library
        target_link_libraries("${PROJECT_NAME}" PRIVATE m)

        # Set up library search paths for different platforms
        if (APPLE)
            # On macOS, check both Homebrew locations
            if (CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
                # Apple Silicon - /opt/homebrew
                list(PREPEND CMAKE_PREFIX_PATH /opt/homebrew)
                set(HOMEBREW_LIB_PATH /opt/homebrew/lib)
            else()
                # Intel Mac - /usr/local
                list(PREPEND CMAKE_PREFIX_PATH /usr/local)
                set(HOMEBREW_LIB_PATH /usr/local/lib)
            endif()
        endif()

        # Find libraries using find_library with fallback to system paths
        find_library(VORBISFILE_LIB vorbisfile HINTS ${HOMEBREW_LIB_PATH})
        find_library(VORBIS_LIB vorbis HINTS ${HOMEBREW_LIB_PATH})
        find_library(OGG_LIB ogg HINTS ${HOMEBREW_LIB_PATH})
        find_library(MPG123_LIB mpg123 HINTS ${HOMEBREW_LIB_PATH})
        find_library(AVFORMAT_LIB avformat HINTS ${HOMEBREW_LIB_PATH})
        find_library(AVCODEC_LIB avcodec HINTS ${HOMEBREW_LIB_PATH})
        find_library(AVUTIL_LIB avutil HINTS ${HOMEBREW_LIB_PATH})
        find_library(SWRESAMPLE_LIB swresample HINTS ${HOMEBREW_LIB_PATH})
        find_library(SPEEX_LIB speex HINTS ${HOMEBREW_LIB_PATH})

        # Link found libraries
        if(VORBISFILE_LIB AND VORBIS_LIB AND OGG_LIB)
            target_link_libraries("${PROJECT_NAME}" PRIVATE ${VORBISFILE_LIB} ${VORBIS_LIB} ${OGG_LIB})
            message(STATUS "VgmStream: Vorbis support enabled")
        else()
            message(STATUS "VgmStream: Vorbis support disabled (libraries not found)")
        endif()

        if(MPG123_LIB)
            target_link_libraries("${PROJECT_NAME}" PRIVATE ${MPG123_LIB})
            message(STATUS "VgmStream: MPEG support enabled")
        else()
            message(STATUS "VgmStream: MPEG support disabled (library not found)")
        endif()

        if(AVFORMAT_LIB AND AVCODEC_LIB AND AVUTIL_LIB AND SWRESAMPLE_LIB)
            target_link_libraries("${PROJECT_NAME}" PRIVATE ${AVFORMAT_LIB} ${AVCODEC_LIB} ${AVUTIL_LIB} ${SWRESAMPLE_LIB})
            message(STATUS "VgmStream: FFmpeg support enabled")
        else()
            message(STATUS "VgmStream: FFmpeg support disabled (libraries not found)")
        endif()

        if(SPEEX_LIB)
            target_link_libraries("${PROJECT_NAME}" PRIVATE ${SPEEX_LIB})
            message(STATUS "VgmStream: Speex support enabled")
        else()
            message(STATUS "VgmStream: Speex support disabled (library not found)")
        endif()
    elseif(WIN32)
        # Windows: Libraries would typically be provided via vcpkg or built separately
        # For now, vgmstream will build with built-in codecs only
        message(STATUS "VgmStream: Windows build with built-in codecs only")
    endif()
endif()

if (WIN32)
    add_compile_definitions(WIN_EXPORT)
endif()

if (MSVC)
    install(FILES $<TARGET_PDB_FILE:${PROJECT_NAME}>
            DESTINATION "${PROJECT_SOURCE_DIR}/bin/Debug/" OPTIONAL) 
endif()

install(TARGETS CUE4Parse-Natives
        CONFIGURATIONS Debug
        RUNTIME DESTINATION "${PROJECT_SOURCE_DIR}/bin/Debug/")
install(TARGETS CUE4Parse-Natives
        CONFIGURATIONS Release
        RUNTIME DESTINATION "${PROJECT_SOURCE_DIR}/bin/Release/")
