# Minimum required version of CMake to be installed in order to build this project.
cmake_minimum_required(VERSION 3.16)

# The project name followed by the language. It is recommended that the folder for this file have the same name as the project.
project(helloworld C)

# The C standard whose features are requested to build this target. Supported values are 90, 99 and 11.
# If you don't care use the latest, 11.
set(CMAKE_C_STANDARD 11)

# Change the output directories for artifacts.
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/lib)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/lib)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${PROJECT_SOURCE_DIR}/lib)

# Add directories so header files can be found
include_directories(helloworld PRIVATE include)

# 1. Specify that the project is a dynamic link library (SHARED).
# 2. Specify what source code files are to be included.
add_library(helloworld SHARED src/helloworld.c include/helloworld.h)