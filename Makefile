
ifndef EXE
	EXE = AwesomeOpossum
endif

ifeq ($(OS),Windows_NT) 
	BINARY_SUFFIX = .exe
	PDB_SUFF = pdb
	BINDINGS_FILE = SIMDBindings.dll

	RENAME_CMD = -ren
	RM_FILE_CMD = del
	RM_FOLDER_CMD = rmdir /s /q

	DETECT_CLANGXX := $(shell where clang++ 2>nul)
	DETECT_CLANG   := $(shell where clang 2>nul)
	DETECT_GPP     := $(shell where g++ 2>nul)
else
	PDB_SUFF = dbg
	BINARY_SUFFIX = 
	BINDINGS_FILE = SIMDBindings.so

	RENAME_CMD = mv
	RM_FILE_CMD = rm
	RM_FOLDER_CMD = rm -rf

	DETECT_CLANGXX := $(shell which clang++ 2>/dev/null)
	DETECT_CLANG   := $(shell which clang 2>/dev/null)
	DETECT_GPP     := $(shell which g++ 2>/dev/null)
endif

FULL_EXE_PATH = $(EXE)$(BINARY_SUFFIX)
RM_PDB = -$(RM_FILE_CMD) $(EXE).$(PDB_SUFF)
RM_BLD_FOLDER = -cd bin && $(RM_FOLDER_CMD) Release && cd ..
RM_OBJ_FOLDER = -$(RM_FOLDER_CMD) obj

INST_SET = native


# Macos doesn't seem to like this parameter and the GenerateBundle task fails during building.
OUT_DIR = -o ./
FIX_OUTPUT = 
ifneq ($(OS),Windows_NT)
	UNAME_S := $(shell uname -s)
	ifeq ($(UNAME_S),Darwin)
		OUT_DIR =
		FIX_OUTPUT = mv ./bin/Release/osx-arm64/publish/AwesomeOpossum ./AwesomeOpossum
	endif
	UNAME_P := $(shell uname -p)
	ifneq ($(filter arm%,$(UNAME_P)),)
		OUT_DIR =
	endif
endif


ifdef EVALFILE
	EVALFILE_STR = -p:EVALFILE=$(EVALFILE)
endif



#  self-contained              .NET Core won't need to be installed to run the binary
#  -p:WarningLevel=0           Silences CS#### warnings during building
#  $(OUT_DIR)                  Should be "-o ./", which outputs the binary in the current directory
#  -p:AssemblyName=$(EXE)      Renames the binary to whatever $(EXE) is.
#  -p:EVALFILE=$(EVALFILE)     Path to a network to be bundled.
COMMON_OPTS = --self-contained -v detailed -p:WarningLevel=0 $(OUT_DIR) -p:AssemblyName=$(EXE) $(EVALFILE_STR) -p:BINDINGS=$(BINDINGS_FILE)

BUILD_OPTS = $(COMMON_OPTS) -c Release 
DATAGEN_OPTS = $(COMMON_OPTS) -c Datagen 

#  -p:PublishAOT=true                 Actually enables AOT
#  -p:PublishSingleFile=false         AOT is incompatible with single file publishing
#  -p:IS_AOT=true                     Sets a variable during runtime signalling AOT is enabled, same to how EVALFILE works.
#  -p:IlcInstructionSet=$(INST_SET)   Instruction set to use, should be "native" if you are only running the binary on your cpu.
AOT_OPTS = $(COMMON_OPTS) -p:PublishAOT=true -p:PublishSingleFile=false -p:IS_AOT=true -p:IlcInstructionSet=$(INST_SET)


CXX := $(firstword $(DETECT_CLANGXX) $(DETECT_CLANG) $(DETECT_GPP))
PUB_CMD = dotnet publish src/AwesomeOpossum/AwesomeOpossum.csproj

.PHONY: release FORCE
.DEFAULT_GOAL := release


$(BINDINGS_FILE): FORCE
	-$(CXX) -std=c++20 -O3 -funroll-loops -march=x86-64-v3 -shared -o ./src/Bindings/$(BINDINGS_FILE) ./src/Bindings/simd.cpp
bindings: $(BINDINGS_FILE)


#  Try building the non-AOT version first, and then try to build the AOT version if possible.
#  This recipe should always work, but AOT requires some additional setup so that recipe may fail.
release: $(BINDINGS_FILE)
	$(PUB_CMD) $(BUILD_OPTS)
	$(FIX_OUTPUT)

datagen: $(BINDINGS_FILE)
	$(PUB_CMD) $(DATAGEN_OPTS)
	$(FIX_OUTPUT)

#  This will/might only succeed if you have the right toolchain
aot:
	-$(PUB_CMD) $(AOT_OPTS)

512: $(BINDINGS_FILE)
	$(PUB_CMD) $(BUILD_OPTS) -p:DefineConstants="AVX512"
	$(FIX_OUTPUT)

aot_512:
	-$(PUB_CMD) $(AOT_OPTS) -p:DefineConstants="AVX512"

all:
	$(MAKE) aot INST_SET=x86-x64
	$(RENAME_CMD) $(FULL_EXE_PATH) $(EXE)-aot-v1$(BINARY_SUFFIX)
	$(MAKE) aot INST_SET=x86-x64-v2
	$(RENAME_CMD) $(FULL_EXE_PATH) $(EXE)-aot-v2$(BINARY_SUFFIX)
	$(MAKE) aot INST_SET=x86-x64-v3
	$(RENAME_CMD) $(FULL_EXE_PATH) $(EXE)-aot-v3$(BINARY_SUFFIX)
	$(MAKE) aot_512 INST_SET=x86-x64-v4
	$(RENAME_CMD) $(FULL_EXE_PATH) $(EXE)-aot-v4$(BINARY_SUFFIX)
	$(MAKE) 512
	$(RENAME_CMD) $(FULL_EXE_PATH) $(EXE)-512$(BINARY_SUFFIX)
	$(MAKE) release
	$(RENAME_CMD) $(FULL_EXE_PATH) $(EXE)$(BINARY_SUFFIX)

clean:
	$(RM_OBJ_FOLDER)
	$(RM_BLD_FOLDER)
	$(RM_PDB)
	
FORCE:
	