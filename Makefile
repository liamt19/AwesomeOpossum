
ifndef EXE
	EXE = AwesomeOpossum
endif

ifdef EVALFILE
	EVALFILE_STR = -p:EVALFILE=$(EVALFILE)
endif

ifeq ($(OS),Windows_NT) 
	BINARY_SUFFIX = .exe
	BINDINGS_FILE = SIMDBindings.dll
	RENAME_CMD = -ren

	DETECT_CLANGXX := $(shell where clang++ 2>nul)
	DETECT_CLANG   := $(shell where clang 2>nul)
	DETECT_GXX     := $(shell where g++ 2>nul)

#   Don't need this on win
	FPIC_MAYBE = 
else
	PDB_SUFF = dbg
	BINDINGS_FILE = SIMDBindings.so
	RENAME_CMD = mv

	DETECT_CLANGXX := $(shell which clang++ 2>/dev/null)
	DETECT_CLANG   := $(shell which clang 2>/dev/null)
	DETECT_GXX     := $(shell which g++ 2>/dev/null)

#   linux wants this
	FPIC_MAYBE = -fPIC
endif

INST_SET = native

#  self-contained              .NET Core won't need to be installed to run the binary
#  -p:WarningLevel=0           Silences CS#### warnings during building
#  -o ./	                   Outputs the binary in the current directory
#  -p:AssemblyName=$(EXE)      Renames the binary to whatever $(EXE) is.
#  -p:EVALFILE=$(EVALFILE)     Path to a network to be bundled.
COMMON_OPTS = --self-contained -v detailed -p:WarningLevel=0 -o ./ -p:AssemblyName=$(EXE) $(EVALFILE_STR) -p:BINDINGS=$(BINDINGS_FILE)

#  -p:PublishAOT=true                 Actually enables AOT
#  -p:PublishSingleFile=false         AOT is incompatible with single file publishing
#  -p:IS_AOT=true                     Sets a variable during runtime signalling AOT is enabled, same to how EVALFILE works.
#  -p:IlcInstructionSet=$(INST_SET)   Instruction set to use, should be "native" if you are only running the binary on your cpu.
AOT_OPTS = $(COMMON_OPTS) -p:PublishAOT=true -p:PublishSingleFile=false -p:IS_AOT=true -p:IlcInstructionSet=$(INST_SET)

BUILD_OPTS = $(COMMON_OPTS) -c Release 
DATAGEN_OPTS = $(COMMON_OPTS) -c Datagen 

PUB_CMD = dotnet publish src/AwesomeOpossum/AwesomeOpossum.csproj

CXX := $(firstword $(DETECT_CLANGXX) $(DETECT_CLANG) $(DETECT_GXX))
BINDINGS_FOLDER = ./src/Bindings
BINDINGS_OPTS = -std=c++20 -O3 -funroll-loops -march=x86-64-v3 -shared $(FPIC_MAYBE)


.PHONY: release FORCE
.DEFAULT_GOAL := release


$(BINDINGS_FILE): FORCE
	-$(CXX) $(BINDINGS_OPTS) -o $(BINDINGS_FOLDER)/$(BINDINGS_FILE) $(BINDINGS_FOLDER)/simd.cpp
bindings: $(BINDINGS_FILE)


#  Try building the non-AOT version first, and then try to build the AOT version if possible.
#  This recipe should always work, but AOT requires some additional setup so that recipe may fail.
release: $(BINDINGS_FILE)
	$(PUB_CMD) $(BUILD_OPTS)

datagen: $(BINDINGS_FILE)
	$(PUB_CMD) $(DATAGEN_OPTS)

#  This will/might only succeed if you have the right toolchain
aot:
	-$(PUB_CMD) $(AOT_OPTS)

512: $(BINDINGS_FILE)
	$(PUB_CMD) $(BUILD_OPTS) -p:DefineConstants="AVX512"

aot_512:
	-$(PUB_CMD) $(AOT_OPTS) -p:DefineConstants="AVX512"

all:
	$(MAKE) aot INST_SET=x86-x64
	$(RENAME_CMD) $(EXE)$(BINARY_SUFFIX) $(EXE)-aot-v1$(BINARY_SUFFIX)
	$(MAKE) aot INST_SET=x86-x64-v2
	$(RENAME_CMD) $(EXE)$(BINARY_SUFFIX) $(EXE)-aot-v2$(BINARY_SUFFIX)
	$(MAKE) aot INST_SET=x86-x64-v3
	$(RENAME_CMD) $(EXE)$(BINARY_SUFFIX) $(EXE)-aot-v3$(BINARY_SUFFIX)
	$(MAKE) aot_512 INST_SET=x86-x64-v4
	$(RENAME_CMD) $(EXE)$(BINARY_SUFFIX) $(EXE)-aot-v4$(BINARY_SUFFIX)
	$(MAKE) 512
	$(RENAME_CMD) $(EXE)$(BINARY_SUFFIX) $(EXE)-512$(BINARY_SUFFIX)
	$(MAKE) release
	$(RENAME_CMD) $(EXE)$(BINARY_SUFFIX) $(EXE)$(BINARY_SUFFIX)

FORCE:
	