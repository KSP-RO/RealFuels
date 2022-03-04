KSPDIR		:= ${HOME}/ksp/KSP_linux
MANAGED		:= ${KSPDIR}/KSP_Data/Managed
GAMEDATA	:= ${KSPDIR}/GameData
MODGAMEDATA	:= ${GAMEDATA}/ModularFuelTanks
PLUGINDIR	:= ${MODGAMEDATA}/Plugins
TSGAMEDATA	:= ${GAMEDATA}/TweakScale
TSPLUGIN	:= ${TSGAMEDATA}/Plugins
TSINTERACT	:= ${TSGAMEDATA}/TweakScaleInteraction

TARGETS		:= bin/modularFuelTanks.dll
#TSTARGETS	:= bin/TweakScale_ModularFuelTanks.dll

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
    Tanks/TankDefinitionSelectionGUI.cs	\
    Tanks/TankWindow.cs				\
	Utilities/Styles.cs				\
    Pumps/RefuelingPump.cs			\
	EditorToolbar.cs				\
	$e

MFT_TS_FILES := \
	tweakscale/TweakScale_ModularFuelSystem.cs	\
	$e

RESGEN2	:= resgen2
GMCS	:= csc
GIT		:= git
TAR		:= tar
ZIP		:= zip

all: version ${TARGETS} ${TSTARGETS}

.PHONY: version
version:
	@./tools/git-version-mft.sh

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
	${GMCS} /highentropyva- /noconfig /nostdlib+ /t:library \
		/r:/usr/lib/mono/4.5-api/mscorlib.dll \
		/r:/usr/lib/mono/4.5-api/System.Core.dll \
		/lib:${MANAGED} \
		/r:Assembly-CSharp.dll,Assembly-CSharp-firstpass.dll \
		/r:UnityEngine.dll,UnityEngine.UI.dll,UnityEngine.CoreModule.dll,UnityEngine.IMGUIModule.dll,UnityEngine.TextRenderingModule.dll,UnityEngine.InputLegacyModule.dll \
		/out:$@ $^

bin/TweakScale_ModularFuelTanks.dll: ${MFT_TS_FILES} bin/modularFuelTanks.dll
	@mkdir -p bin
	${GMCS} -t:library -lib:${TSPLUGIN},${MANAGED} \
		-r:Assembly-CSharp.dll,Assembly-CSharp-firstpass.dll,UnityEngine.dll \
		-r:Scale_Redist.dll \
		-r:bin/modularFuelTanks.dll \
		-out:$@ ${MFT_TS_FILES}

clean:
	rm -f ${TARGETS} assembly/AssemblyInfoMFT.cs

install: all
	mkdir -p ${PLUGINDIR}
	cp ${TARGETS} ${PLUGINDIR}
	#mkdir -p ${TSINTERACT}
	#cp ${TSTARGETS} ${PLUGINDIR}

.PHONY: all clean info install
