KSPDIR		:= ${HOME}/ksp/KSP_linux
MANAGED		:= ${KSPDIR}/KSP_Data/Managed
GAMEDATA	:= ${KSPDIR}/GameData
MODGAMEDATA	:= ${GAMEDATA}/ModularFuelTanks
PLUGINDIR	:= ${MODGAMEDATA}/Plugins
APIEXTDATA	:= ${PLUGINDIR}
TSGAMEDATA	:= ${GAMEDATA}/TweakScale
TSPLUGIN	:= ${TSGAMEDATA}/Plugins
TSINTERACT	:= ${TSGAMEDATA}/TweakScaleInteraction

TARGETS		:= bin/modularFuelTanks.dll
TSTARGETS	:= bin/TweakScale_ModularFuelTanks.dll

MFT_FILES := \
    assembly/AssemblyInfoMFT.cs		\
    assembly/Checkers.cs			\
	assembly/VersionReport.cs		\
    Tanks/FuelInfo.cs				\
    Tanks/FuelTank.cs				\
    Tanks/FuelTankList.cs			\
	Tanks/MFSSettings.cs			\
    Tanks/ModuleFuelTanks.cs		\
    Tanks/TankDefinition.cs			\
    Tanks/TankDefinitionList.cs		\
    Tanks/TankWindow.cs				\
    Pumps/RefuelingPump.cs			\
	EditorToolbar.cs				\
	$e

MFT_TS_FILES := \
	tweakscale/TweakScale_ModularFuelSystem.cs	\
	$e

RESGEN2	:= resgen2
GMCS	:= gmcs
GIT		:= git
TAR		:= tar
ZIP		:= zip

all: version ${TARGETS} ${TSTARGETS}

.PHONY: version
version:
	@./tools/git-version.sh

info:
	@echo "Modular Fuel Tanks Build Information"
	@echo "    resgen2:  ${RESGEN2}"
	@echo "    gmcs:     ${GMCS}"
	@echo "    git:      ${GIT}"
	@echo "    tar:      ${TAR}"
	@echo "    zip:      ${ZIP}"
	@echo "    KSP Data: ${KSPDIR}"
	@echo "    Plugin:   ${PLUGINDIR}"

bin/modularFuelTanks.dll: ${MFT_FILES}
	@mkdir -p bin
	${GMCS} -t:library -lib:${APIEXTDATA},${MANAGED} \
		-r:Assembly-CSharp,Assembly-CSharp-firstpass,UnityEngine \
		-r:KSPAPIExtensions \
		-out:$@ $^

bin/TweakScale_ModularFuelTanks.dll: ${MFT_TS_FILES} bin/modularFuelTanks.dll
	@mkdir -p bin
	${GMCS} -t:library -lib:${TSPLUGIN},${MANAGED} \
		-r:Assembly-CSharp,Assembly-CSharp-firstpass,UnityEngine \
		-r:Scale_Redist.dll \
		-r:bin/modularFuelTanks.dll \
		-out:$@ ${MFT_TS_FILES}

clean:
	rm -f ${TARGETS} assembly/AssemblyInfoMFT.cs

install: all
	mkdir -p ${PLUGINDIR}
	cp ${TARGETS} ${PLUGINDIR}
	mkdir -p ${TSINTERACT}
	cp ${TSTARGETS} ${PLUGINDIR}

.PHONY: all clean info install
