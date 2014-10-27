KSPDIR		:= ${HOME}/ksp/KSP_linux
MANAGED		:= ${KSPDIR}/KSP_Data/Managed
GAMEDATA	:= ${KSPDIR}/GameData
MODGAMEDATA	:= ${GAMEDATA}/ModularFuelTanks
PLUGINDIR	:= ${MODGAMEDATA}/Plugins
APIEXTDATA	:= ${PLUGINDIR}
TSGAMEDATA	:= ${GAMEDATA}/TweakScale
TSPLUGIN	:= ${TSGAMEDATA}/Plugins
TSINTERACT	:= ${TSGAMEDATA}/TweakScaleInteraction

TARGETS		:= modularFuelTanks.dll
TSTARGETS	:= TweakScale_ModularFuelTanks.dll

MFT_FILES := \
    AssemblyInfoMFT.cs			\
    Checkers.cs					\
    HeatPump.cs					\
    ModularFuelTanks.cs			\
    ModularFuelPartModule.cs	\
    RefuelingPump.cs			\
	MFSSettings.cs				\
	VersionReport.cs			\
	$e

MFT_TS_FILES := \
	TweakScale_ModularFuelSystem.cs	\
	$e

RESGEN2	:= resgen2
GMCS	:= gmcs
GIT		:= git
TAR		:= tar
ZIP		:= zip

all: version ${TARGETS}

.PHONY: version
version:
	@./git-version.sh

info:
	@echo "Modular Fuel Tanks Build Information"
	@echo "    resgen2:  ${RESGEN2}"
	@echo "    gmcs:     ${GMCS}"
	@echo "    git:      ${GIT}"
	@echo "    tar:      ${TAR}"
	@echo "    zip:      ${ZIP}"
	@echo "    KSP Data: ${KSPDIR}"
	@echo "    Plugin:   ${PLUGINDIR}"

modularFuelTanks.dll: ${MFT_FILES}
	${GMCS} -t:library -lib:${APIEXTDATA},${MANAGED} \
		-r:Assembly-CSharp,Assembly-CSharp-firstpass,UnityEngine \
		-r:KSPAPIExtensions \
		-out:$@ $^

TweakScale_ModularFuelTanks.dll: ${MFT_TS_FILES} modularFuelTanks.dll
	${GMCS} -t:library -lib:${TSPLUGIN},${MANAGED} \
		-r:Assembly-CSharp,Assembly-CSharp-firstpass,UnityEngine \
		-r:Scale_Redist.dll \
		-r:modularFuelTanks.dll \
		-out:$@ ${MFT_TS_FILES}

clean:
	rm -f ${TARGETS} AssemblyInfoMFT.cs

install: all
	mkdir -p ${PLUGINDIR}
	cp ${TARGETS} ${PLUGINDIR}
	mkdir -p ${TSINTERACT}
	cp ${TSTARGETS} ${TSINTERACT}

.PHONY: all clean info install
