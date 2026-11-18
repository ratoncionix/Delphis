# Delphis — Distribution-agnostic Linux build and installation.
# Run `make help` for a summary of targets and variables.

PREFIX         ?= /usr/local
DESTDIR        ?=
BINDIR         ?= $(PREFIX)/bin
LIBDIR         ?= $(PREFIX)/lib/delphis
DATADIR        ?= $(PREFIX)/share

PYTHON         ?= python3
DOTNET         ?= $(shell command -v dotnet 2>/dev/null || ([ -x "$(HOME)/.dotnet/dotnet" ] && echo "$(HOME)/.dotnet/dotnet" || echo dotnet))
DOTNET_HOST    ?=
WITH_DTE       ?= 1
SELF_CONTAINED ?= 0
CONFIGURATION  ?= Release
BUILD_DIR      ?= build
RID            ?=
SEVENZIP_LIB   ?=

DOTNET_ENV := DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

ifeq ($(SELF_CONTAINED),1)
ifeq ($(strip $(RID)),)
UNAME_M := $(shell uname -m)
ifeq ($(UNAME_M),x86_64)
ARCH := x64
else ifeq ($(UNAME_M),aarch64)
ARCH := arm64
else ifeq ($(UNAME_M),arm64)
ARCH := arm64
else ifneq ($(filter armv7%,$(UNAME_M)),)
ARCH := arm
else
ARCH := x64
endif
IS_MUSL := $(shell ldd --version 2>&1 | grep -qi musl && echo 1 || echo 0)
ifeq ($(IS_MUSL),1)
RID := linux-musl-$(ARCH)
else
RID := linux-$(ARCH)
endif
endif
PUBLISH_FLAGS := -c $(CONFIGURATION) --self-contained -r $(RID)
else
PUBLISH_FLAGS := -c $(CONFIGURATION) --no-self-contained
endif

SEVENZIP_CANDIDATES := \
	/usr/lib/7zip/7z.so \
	/usr/lib/p7zip/7z.so \
	/usr/lib/x86_64-linux-gnu/7z.so \
	/usr/lib/aarch64-linux-gnu/7z.so \
	/usr/lib64/p7zip/7z.so \
	/usr/libexec/p7zip/7z.so \
	/usr/lib64/7z.so \
	/usr/local/lib/7z.so

ifeq ($(strip $(SEVENZIP_LIB)),)
SEVENZIP_LIB := $(firstword $(wildcard $(SEVENZIP_CANDIDATES)))
endif

define render-delphis-launcher
	sed -e 's|@LIBDIR@|$(1)|g' \
	    -e 's|@BINDIR@|$(2)|g' \
	    -e 's|@PYTHON@|$(3)|g' \
	    packaging/delphis.sh.in > $(4)
	chmod 0755 $(4)
endef

define render-dotnet-launcher
	sed -e 's|@NAME@|$(1)|g' \
	    -e 's|@LIBDIR@|$(2)|g' \
	    -e 's|@BINDIR@|$(3)|g' \
	    -e 's|@REL_EXE@|$(4)|g' \
	    -e 's|@DOTNET_HOST@|$(5)|g' \
	    packaging/dotnet-launcher.sh.in > $(6)
	chmod 0755 $(6)
endef

.PHONY: all build cli dte launchers test run install uninstall clean help

all: build

ifeq ($(WITH_DTE),1)
build: cli dte launchers
else
build: cli launchers
endif

cli:
	$(DOTNET_ENV) $(DOTNET) publish src/Delphis.Cli/Delphis.Cli.csproj $(PUBLISH_FLAGS) -o "$(BUILD_DIR)/cli"

dte:
	$(DOTNET_ENV) $(DOTNET) publish DolphinTextureExtraction/DolphinTextureExtraction.csproj $(PUBLISH_FLAGS) -o "$(BUILD_DIR)/dte"
	@if [ -n "$(SEVENZIP_LIB)" ] && [ -e "$(SEVENZIP_LIB)" ]; then \
		install -d "$(BUILD_DIR)/dte/x64"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(BUILD_DIR)/dte/7z.so"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(BUILD_DIR)/dte/7z.dll"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(BUILD_DIR)/dte/7z-x64.dll"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(BUILD_DIR)/dte/x64/7z.so"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(BUILD_DIR)/dte/x64/7z.dll"; \
	fi

launchers:
	install -d "$(BUILD_DIR)/bin"
	ln -sfn "$(CURDIR)/src" "$(BUILD_DIR)/python"
	$(call render-delphis-launcher,$(CURDIR)/$(BUILD_DIR),$(CURDIR)/$(BUILD_DIR)/bin,$(PYTHON),"$(BUILD_DIR)/bin/delphis")
	$(call render-dotnet-launcher,delphis-cli,$(CURDIR)/$(BUILD_DIR),$(CURDIR)/$(BUILD_DIR)/bin,cli/delphis-cli,$(DOTNET_HOST),"$(BUILD_DIR)/bin/delphis-cli")
ifeq ($(WITH_DTE),1)
	$(call render-dotnet-launcher,dte,$(CURDIR)/$(BUILD_DIR),$(CURDIR)/$(BUILD_DIR)/bin,dte/DolphinTextureExtraction.tool,$(DOTNET_HOST),"$(BUILD_DIR)/bin/dte")
endif

test:
	$(DOTNET_ENV) $(DOTNET) run --project tests/Delphis.Tests/Delphis.Tests.csproj
	$(PYTHON) -m compileall -q src/delphis_gui
	QT_QPA_PLATFORM=offscreen PYTHONPATH=src $(PYTHON) -m unittest discover -s tests -p "test_*.py" -v

run: build
	PATH="$(CURDIR)/$(BUILD_DIR)/bin:$$PATH" PYTHONPATH="$(CURDIR)/src" "$(PYTHON)" -m delphis_gui

install: build
	install -d "$(DESTDIR)$(BINDIR)" \
	           "$(DESTDIR)$(LIBDIR)/cli" \
	           "$(DESTDIR)$(LIBDIR)/python/delphis_gui" \
	           "$(DESTDIR)$(DATADIR)/applications" \
	           "$(DESTDIR)$(DATADIR)/icons/hicolor/scalable/apps"
	rm -rf "$(DESTDIR)$(LIBDIR)/cli"
	install -d "$(DESTDIR)$(LIBDIR)/cli"
	cp -a "$(BUILD_DIR)/cli/." "$(DESTDIR)$(LIBDIR)/cli/"
	rm -rf "$(DESTDIR)$(LIBDIR)/python/delphis_gui"
	install -d "$(DESTDIR)$(LIBDIR)/python/delphis_gui"
	(cd src/delphis_gui && find . \( -name __pycache__ -o -name '*.pyc' -o -name '*.pyo' \) -prune \
		-o -type d -exec install -d "$(DESTDIR)$(LIBDIR)/python/delphis_gui/{}" \; \
		-o -type f -exec install -m 0644 "{}" "$(DESTDIR)$(LIBDIR)/python/delphis_gui/{}" \;)
	install -Dm644 VERSION "$(DESTDIR)$(LIBDIR)/python/delphis_gui/VERSION"
	$(call render-delphis-launcher,$(LIBDIR),$(BINDIR),$(PYTHON),"$(DESTDIR)$(BINDIR)/delphis")
	$(call render-dotnet-launcher,delphis-cli,$(LIBDIR),$(BINDIR),cli/delphis-cli,$(DOTNET_HOST),"$(DESTDIR)$(BINDIR)/delphis-cli")
ifeq ($(WITH_DTE),1)
	rm -rf "$(DESTDIR)$(LIBDIR)/dte"
	install -d "$(DESTDIR)$(LIBDIR)/dte"
	cp -a "$(BUILD_DIR)/dte/." "$(DESTDIR)$(LIBDIR)/dte/"
	@if [ -n "$(SEVENZIP_LIB)" ] && [ -e "$(SEVENZIP_LIB)" ]; then \
		install -d "$(DESTDIR)$(LIBDIR)/dte/x64"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(DESTDIR)$(LIBDIR)/dte/7z.so"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(DESTDIR)$(LIBDIR)/dte/7z.dll"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(DESTDIR)$(LIBDIR)/dte/7z-x64.dll"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(DESTDIR)$(LIBDIR)/dte/x64/7z.so"; \
		ln -sfn "$(SEVENZIP_LIB)" "$(DESTDIR)$(LIBDIR)/dte/x64/7z.dll"; \
	fi
	$(call render-dotnet-launcher,dte,$(LIBDIR),$(BINDIR),dte/DolphinTextureExtraction.tool,$(DOTNET_HOST),"$(DESTDIR)$(BINDIR)/dte")
endif
	install -Dm644 packaging/delphis.desktop "$(DESTDIR)$(DATADIR)/applications/delphis.desktop"
	install -Dm644 assets/icons/delphis.svg "$(DESTDIR)$(DATADIR)/icons/hicolor/scalable/apps/delphis.svg"

uninstall:
	rm -f "$(DESTDIR)$(BINDIR)/delphis" \
	      "$(DESTDIR)$(BINDIR)/delphis-cli" \
	      "$(DESTDIR)$(BINDIR)/dte" \
	      "$(DESTDIR)$(DATADIR)/applications/delphis.desktop" \
	      "$(DESTDIR)$(DATADIR)/icons/hicolor/scalable/apps/delphis.svg"
	rm -rf "$(DESTDIR)$(LIBDIR)"

clean:
	rm -rf "$(BUILD_DIR)"

help:
	@printf "Delphis build & install targets:\n"
	@printf "  make              Build delphis-cli and bundled DTE into $(BUILD_DIR)/\n"
	@printf "  make test         Run automated C# and Python checks\n"
	@printf "  make run          Launch the PyQt6 GUI from the source tree\n"
	@printf "  make install      Install into PREFIX (default: $(PREFIX))\n"
	@printf "  make uninstall    Remove files installed into PREFIX\n"
	@printf "  make clean        Remove $(BUILD_DIR)/\n\n"
	@printf "Useful variables:\n"
	@printf "  PREFIX=$(PREFIX)  DESTDIR=$(DESTDIR)  PYTHON=$(PYTHON)  DOTNET=$(DOTNET)\n"
	@printf "  WITH_DTE=$(WITH_DTE)  SELF_CONTAINED=$(SELF_CONTAINED)  RID=$(RID)\n"
	@printf "  DOTNET_HOST=$(DOTNET_HOST)  SEVENZIP_LIB=$(SEVENZIP_LIB)\n"
