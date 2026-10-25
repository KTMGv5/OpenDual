cmake_minimum_required(VERSION 3.20)
project(OpenDualDriver LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include_directories(include)

set(SOURCES
    src/HidDevice.cpp
    src/TouchpadEngine.cpp
    src/AudioSubsystem.cpp
    src/VirtualGamepad.cpp
    src/IpcServer.cpp
    src/ControllerEngine.cpp
    src/main.cpp
)

set(HEADERS
    include/Common.hpp
    include/DualSenseProtocol.hpp
    include/DS4Protocol.hpp
    include/HidDevice.hpp
    include/TouchpadEngine.hpp
    include/AudioSubsystem.hpp
    include/VirtualGamepad.hpp
    include/IpcServer.hpp
    include/ControllerEngine.hpp
)

add_executable(OpenDualDriver ${SOURCES} ${HEADERS})

target_link_libraries(OpenDualDriver PRIVATE
    setupapi
    hid
    ole32
    winmm
    avrt
)

set_target_properties(OpenDualDriver PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL"
)
