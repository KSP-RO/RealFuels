**** RealFuels ****
by NathanKell
Contributors: Chestburster, Starwaster, taniwha, swamp_ig, ialdabaoth (obviously, but back again!)
ialdabaoth (who is awesome) created Modular Fuels, and this is a fork of the RealFuels branch.

License remains CC-BY-SA as modified by ialdabaoth.
Source: https://github.com/NathanKell/ModularFuelSystem (shared repository between RF and Modular Fuel Tanks).

Also included (all are required)
* Module Manager by sarbian, swamp_ig, and ialdabaoth. See thread for details: http://forum.kerbalspaceprogram.com/threads/55219
* Community Resource Pack: See thread for details: http://forum.kerbalspaceprogram.com/threads/91998
* SolverEngines by NathanKell and blowfish. See thread for details: http://forum.kerbalspaceprogram.com/threads/122976


Includes ullage simulation code based on that by HoneyFox in EngineIgnitor, reused under MIT License. See end of readme for license details.
Includes ConfigNodeExtensions by stupid_chris, license CC-BY-SA.

DESCRIPTION:
Real Fuels does the following:
* It converts resources to use 1 unit = 1 liter at STP, and changes tank capacity to accord with that.
* It allows any supported tank to be filled with exactly how much or how little fuel you want, of whatever type you want (though different tanks may allow or disallow certain fuels; jet fuel tanks won't take oxidizer for instance). Tank dry masses are set to replicate real-world rocket stages.
* Real world fuels are added, and engines/RCS can use them, with realistic stats (NOTE: You NEED an engine pack in order for engines/RCS to use the new fuels).
* Engines/RCS can have multiple configurations (for example, an upper stage could support a kerosene + liquid oxygen mode, and a liquid hydrogen + liquid oxygen mode). These modes have different thrust, Isp, etc.
* Engines/RCS can have varying techlevels: different performance characteristics for the same part, based on the techlevel you select (techlevels can be tied to R&D nodes for career games).
* Configs and techlevels can have their own unlock costs
* Engines can have limited throttling, and can have limited ignitions.
* Engines can be subject to ullage requirements.
* For advanced features, engines use ModuleEnginesRF (powered by SolverEngines).

INSTALL INSTRUCTIONS:
1. Delete any existing ModularFuelTanks folder or RealFuels folder in your KSP/GameData folder. Remove CommunityResourcePack and SolverEngines if they exist as well. This is VITAL.
2. Extract this archive to your KSP/GameData folder
3. Download and install an engine pack. Your choices currently are Stockalike RF Configs or RealismOverhaul. See the second post in the RealFuels thread for information.

USAGE:
You can access RF-related GUIs ingame by tweakables, or by going to the Action Group Editor mode in the VAB/SPH (i.e. where you assign things to action groups) and clicking on a tank, engine, or RCS module. If supported, the GUI will appear.

For tanks:
At the top will appear the total tank mass (wet), the tank dry mass, the available, used, and total volume (in liters). Below appears the set of resources that may be added to the tank, and the current amounts and max amounts (if any).
If there are engines on the vessel, and available volume in the tank, autoconfigure buttons will appear at the top of the list, one for each fuel mixture used by the engines on the vessel. When you hover the cursor over an autoconfigure button, a tooltip will appear showing the engines that use that mixture. Click an autoconfigure button to automatically configure remaining volume for that mixture.
Note that gases and electric charge have multiple "units" per tank liter, since gases are given in liters at STP but stored under pressure, and electric charge is in kJ.
When you right-click on a tank, you can also access the 'remove all' and the 'configure for' buttons.

For engines/RCS:
At the top are buttons for changing the current engine's configuration. Then there are the buttons for changing techlevel. They will have X if a change in that direction is unavailable. Below that are stats for the current config and TL. NOTE that if your RCS uses a fuel that is set to STACK_PRIORITY_SEARCH rather than ALL_VESSEL (anything except MonoPropellant) you need to have fuel feeding your RCS thrusters (i.e. treat them like radial engines). It is suggested you get CrossFeedEnabler to help with this.


AN OVERVIEW OF FUEL TYPES AND TANK TYPES AND TECH LEVELS/ENGINE TYPES AND UPGRADE COSTS ARE BELOW THE CHANGELOG

==========
Changelog:
v10.6
* New throttling behavior. Old bugs with it were fixed, and now there is a proper delay while thrust builds up, when igniting a liquid engine. It will take about two seconds for an F-1 class engine to build up to full thrust. The rate can be tweaked, set throttleResponseRate in the ModuleEnginesRF (or in a CONFIG that's applied to one). By default when the current throttle is within 0.5% of the desired throttle, the engine clamps to the desired throttle. Further, when setting 0 throttle, the engine instantly shuts off (the latter will change, later). WORD TO THE WISE: Use launch clamps, and make sure your engines are at full thrust before disengaging the clamps!
* Fix for sometime "says stable but fails to ignite" issue. Supreme thanks for stratochief66 for figuring out where to look!
* Increase ullage acceleration threshold. Cryo stages will no longer keep themselves at Very Stable, but it won't take much thrust to ullage them.
* Add tanks for the other Ethanol resources.

v10.5.1
* Fix issue with CONFIG entry costs being lost.

v10.5
* Update to SolverEngines v1.9.
* Auto-remove Interstellar Fuel Switch or FS Fuel Switch modules on parts that have RF tank modules on them too.
* Add a new setting to disable natural diffusion when there is acceleration greater than (this threshold). Makes ullaging stages easier since only minimal acceleration is needed (it just can take a while).
* Fix some flameout issues (and the 'flameout' sound on load with a pressure-fed engine).
* Fix issue with a typo in ullage sim's rotation bit. Spinning axially will no longer cause ullage-outs so rapidly.
* Attempt to load/save 'ignited' property.
* Added other solid fuels to 'instant throttling' list.
* Tellion: more NF Propulsion support, MkIV support.
* Update engine/TL upgrade tracking to not keep the costs persistent (i.e. changing files no longer needs starting a new save).
* Support maxSubtraction for entryCostSubtractors, do all subtraction(s) before all multiplications.
* Update all heat animations on ModuleEnginesRF parts to use new animation module from SolverEngines.
* Fix a big bug with ignition in CONFIG nodes. Now tracked properly.
* Display pressure/ullage/ignitions info in GetInfo for ModuleEnginesRF and for MEC's alternate configs info text (if it differs from default config).

v10.4.9
* Hotfix for the hotfix.
* Don't load/save ullage sim data in editor.
* NOTE: You may have to detach and reattach your engines in saved craft. Also, action groups involving engines will need to be remade.

v10.4.8
* Hotfix for duplicated actions on engines (requires SolverEngines 1.7).

v10.4.7
* Tellion: add NF Spacecraft and Construction tank configs.
* Un-break the cost of CONFIG upgrades (and a bunch of other settings).

v10.4.6
* Hotfix for PP Proc SRB interaction.

v10.4.5
* Update to CRP 4.3. Remove no-longer-needed hsp changes.
* Add a fix to TEATEB flow mode until next CRP.
* Clean up behavior when igntions are specified in a CONFIG.
* Make the simulateUllage setting be respected.
* Add a limitedIgnitions setting (which can be set to false).
* Default origTechLevel to -1 to avoid an issue on engine configuration change.
* SolverEngines update fixes "can't activate when shielded" issue.

v10.4.4
* Fix bug where tanks that had flow disabled in partactionmenu were not counted for pressure-fed checks.
* Fix bug where legacy EI configs were not being applied.
* Fix issues with fuel ratio for ullage.
* Show ignitions and pressure-fed-ness in Editor tooltips.

v10.4.3
* Fix an NRE in ullage code in editor.
* Fix a bug with multFlow not being used correctly.

v10.4.2
* Repack to include correct KSPAPIExtensions

v10.4.1
* Fix throttle/ignition for throttle-locked (solids).
* Fix to report nominal propellant status when pressurefed OK and ullage disabled.

v10.4
* Update for KSP 1.0.4.
* Ullage and limited ignitions now included, works like EngineIgnitor though the module configuration is different. If you set EI configs in your ModuleEngineConfigs CONFIG nodes, however, those will be read just fine by RF.
* Spaceplane part volumes and tank types tweaked for better utility.
* TweakScale support for engines.

v10.3.1
* Readme update,
* Fixed an NRE that killed loading under certain circumstances.
* Do a better search for which engines are on a ship.

v10.3
* Added cost to unlock new configurations and new TLs for engines. Cost can be fully configured both globally and per part config, and can take from funds and/or science. See UPGRADE COSTS below.
* Add hsp for Furfurfyl Alcohol.
* Make the GUI draggable in action editor too.
* Update for latest SolverEngines.
* Clamp chamber temp to be no lower than part internal temp.
* Allow random variation in fuel flow (defaults to 0 variation, set varyThrust to a >0 number to enable). Thrust variation is multiplicative, and will be in the range +/- (global varyThrust * ModuleEnginesRF.varyThrust). ModuleEnginesRF.varyThrust defaults to 1.0. Example: you set global varyThrust (in RFSETTINGS) to 0.008. Then all engines that use ModuleEnginesRF will have +/- 0.8% variation in their thrust during flight.
* Fix a bug in detecting engines to autoconfigure for: let's check ourselves too.
* Fix issues in basemass / basecost overrides in ModuleFuelTank nodes.

v10.2
* Allow time-based thrust curves.
* Fix thrust curves to actually work.
* Add more specific heat capacities for resources.
* Fix NaN with SolverEngines.
* basemass now defaults for being for the entire part, not just the utilized portion (i.e. utilization slider is ignored for basemass, always 100%). This will marginally increase tank masses. This can be toggled in MFSSettings.cfg.
* Update volume and type of some spaceplane adapter tanks.
* (Finally!) add nacelleBody and radialEngineBody.
* Fix typo with large Xenon tank; properly patched now.
* Support any case for 'Full' when setting amount in a TANK.
* Fix when engine configs could sometimes be empty.
* Fix up boiloff loss rates for KSP 1.0 heating.
* Add some heat loss when propellant boils off (due to vaporization heat).

v10.1
* Added specific heats for most of the resources (thanks stratochief!).
* Revised temperature gauge for rocket engines.
* Set tanks with cryo propellants to their boiling points during prelaunch when pumps (i.e. launch clamps) are connected, so they don't start way above BP.
* Make life support waste resources not fillable.
* Compatibility with Ven's Stock Revamp for the RF cloned parts

v10.0
* SAVE-BREAKING.
* KSP 1.0 support.
* Remove thermal fin and radiator.
* Use Community Resource Pack for our resources, don't add resources in RF.
* Xenon tank type is removed; all of these tanks use ElectricPropulsion now.
* Now have multiple different solid fuel resources, and thus multiple different solid fuel tank types.
* Add module info in the editor tooltip for tanks
* Engine info / configuration info will only display for the master ModuleEngineConfigs on the part.
* Disable MEC event firing on configuration change (was killing FAR).
* Updating an engine config will properly propagate to symmetry counterparts.
* Updating the engine config of an isMaster=true module can propagate changes to isMaster=false modules on the same part (and will propagate properly across symmetry counterparts). Example: Change the main engine config and the vernier config will auto-update. Done by, for each CONFIG, adding an OtherModules {} node. Inside are key-value pairs, where key = engineID of other module and value is config to switch to.
* Separate settings for RF engines (RFSETTINGS) and tanks (still MFSSETTINGS).
* Remove deprecated old version of hybrid engines (the one that is essentially MultiModeEngine).
* Speed up ModuleEngineConfigs a lot, cut the excess bits from ModuleHybridEngines.
* Fix issue with heat multiplier
* Rewrite floatcurve-modder to respect tangents.
* Massively refactor engines code. RealFuels, like AJE, will use an engine solver now. The new engine module (ModuleEnginesRF) handles thrust curves, throttle speed, emission and internal engine temperature, automatically extending Isp curves to 0 Isp, etc.
* MEC (and MHE) default to using weak typing: type = ModuleEngines means apply to ModuleEngines or anything derived from it (same for ModuleRCS etc). You can disable this feature per-module if needed.

v9.1
* Fixed stock RCS and xenon tank volumes.
* Don't pump into tanks if their flow has been turned off.
* Clamp utilization slider to 1% (avoids a divide-by-zero).
* Fix typos in Tantares tanks. Thanks komodo!
* Unlock input when the RF GUI disappears (fixes a bug where clicking can be locked).

v9.0
* Added notes on tank types to the notes section at the bottom of the readme.
* Switch to taniwha's refactor of MFT (should fix a lot of bugs).
* Refactor TechLevels, fix longstanding techlevel override bug.
* Change so it's not ElectricCharge the resource that costs funds, but rather how much capacity you have.
* Fixed bug where an engine that shares FX betwqeen CONFIGs could have its FX shut down.
* Removed deprecated StretchyTanks clones.
* Add techRequired support for resources and for tank types.
* Add gimbal support to modular engines (TechLevel changing or CONFIG changing can change gimbal). Supports only stock gimbal for now.
* Cost for engines increases with TechLevel.

v8.4
*Fixed stock KSP mass calculation (for engineer's report and for pad limits).
*Added TestFlight integration support.
*Remove KSPI config so that RF will no longer be a bottleneck.
*Add support for per-CONFIG effects settings (running, power, or directThrottle FX not listed in the current CONFIG but listed in other CONFIGs will be turned off).
*aristurtle: add support for TurboNisuReloaded.
*Maeyanie: add missing SXT LMiniAircaftTail, Tantares tanks.
*Raptor831: add Firespitter helicopter crewtank.
*Raptor831, Starwaster: Fix & to , for MM.
*ImAHungryMan: add support for missing tanks in RS Capsuldyne (Taurus), Nertea's Mk IV system, RetroFuture, SXT; Convert some Mk2 and Mk3 tanks to cryogenic and add missnig Mk3 tanks.

v8.3
*Update to .90 (thanks ckfinite and taniwha)
*Don't fire editor events when we shouldn't
*Add cost info to engine change GUI
*Show engine configs that are not avialable due tech (not having that node)

v8.2
*Update heat pumps (thanks Starwaster)
*Fix added parts to be MM clones
*taniwha: lots of refactoring
*regex: add lots of missing tanks (FASA, HGR, NP2, RLA, SXT)
*dreadicon: improved KSPI config
*camlost: RetroFuture tank configs
*TriggerAu: include icons for ARP in RealFuels rather than in ARP
*taniwha: correct tank cost calcs
*Raptor831: Add missing NP2, HGR tanks; Add Taurus pod/SM tanks
*lilienthal: fix Thermal Fin description
*Starman-4308: Add configs for Modular Rocket System
*Add support for the 0.625m tanks in Ven's Stock Part Revamp
*Show tank/fuel cost in GUI
*Lower Solid Fuel and ElectricCharge costs (oops)
*Fix so science sandbox is still detected as "has R&D tree"
*Add setting for unit label
*A Modular Engine will switch to the first available config if its current config is not available (due to requiring a tech tree node you don't have researched).
*Starman4308: SpaceY tank configs
*Starwaster: configs for TT's Mk2 nosecone and Nertea's MkIV system.

v8.1
*Fix my stupidity; I forgot to change some of RF's own patches to account for the new resource names.
*camlost: fix tank name for new FS.
*Fix applying changes to resource amounts more than once on TweakScale rescale.
*Change FS fuselages to calculate their own basemass.
*Fix a GUI click-locking issue.
*Remove old/broken KSPI interaction config; a new one is in the works by dreadicon and Northstar1989.

v8.0
*SAVE-BRREAKING - however, regex made a tool to attempt to update saves. Post on the thread if you want to try it out.
*Redone resources by regex: some resources have changed name, a couple generic ones have been made into various specific ones, and many new (often scary) resources have been added. Note that all resources now have costs, normalized for 1 fund = $1000 in 1965 US Dollars.
*Redone tank masses to better match extant launch vehicles. LV performance will decrease, since mostly that means dry mass went up.
*Cryogenic tank type is now modeled on the Delta IV and Ariane 5 cores; the Shuttle ET is best modeled as a BalloonCryo tank with some ballast.
*RCS, RCSHighEfficiency, Jet, and Oxy tanks are deprecated. Fuselage is ServiceModule with slightly higher mass (used for planes) and Structural is Default with the same basemass as Fuselage (used for planes when the tank doesn't need to be highly pressurized, or have electricity, life support, etc.)
*Updated to MM 2.5.1 (and added FOR[] to the patches).
*Updated SPP patches for .25, redid volumes.
*Fixed for current Tweakscale; should now finally work right!
*taniwha: refueling pump now works, costs funds, and only works if at KSC.
*Disabled when incompatible KSP detected (.24 or any non-.25 version, .25Winx64), though unlocked versions are available on request by PM (on condition of no redistribution and no support)

v7.4 \/
*B9 configs removed from RF; they are included in B9 itself.
*Fixed so tank-switching can be done after a database reload
*Added procedural cost, with taniwha
*Fixed refueling pumps again, with taniwha (they respect flow and flow type, and cost funds)
*Autoconfig buttons moved to the top of the list, and fixed (will now treat jets etc properly, and both multimode modes)
*Supports multiple ModuleEngineConfigs per part (i.e. Multi mode engines, engine+RCS, etc)
*Maeyanie: add support for SXT and KAX
*Removed some unneeded Firespitter entries
*Supports Tweakscale again, internally. NOTE if you do not have tweakscale, you will get a harmless exception in the log about failing to load Tweakscale_Realfuels.dll. Ignore it.
*Made all RCS tanks into ServiceModule tanks (finally); deprecated old RCS tank type.
*Aristurtle Support blackheart's AJKD and KSLO mods.
*Raptor831: Support for Kommit Nucleonics, KSPI improvement, Near Future

v7.3 \/
*Change versioning to 0.x.y internally.
*Only apply SF patch to parts with SolidFuel (less log spam)
*Fixed KIDS interoperabilty
*Fixed so that throttle and massMult work for CONFIGs that don't use techlevels.
*thrustCurves now modify heat in proportion to thrust
*Launch clamp refuel pumps default to off (note: the value is persistent, so if you have a saved craft from before this change, and it has clamps, they will still be defaulting to on).
*aristurtle: fix bugs in HGR config, fix KW typos, add more KW tanks, add KSO tanks
*Increase B9 spaceplane parts fuel capacity by 1.5x to bring them more in line with their physical volume, switch them to using Fuselage tank type
*Fix right-click menu displays regarding rated thrust; now there is only one display, and it only shows up when there is a thrustCurve in use.

v7.2 \/
*Fix bug with auto-rescaling of solid fuel resources.
*aristurtle: add support for 5m KW tanks
*Add back ModuleRCSFX support (get ModuleRCSFX from Realism Overhaul)
*Fix the exposed Isp multipliers (for interoperability)
*Add more failsafe checks and ways to avoid issues in Win x64
*Add support for thrustCurve in CONFIG (x = ratio of currentFuel/maxFuel, y = multiplier to thrust)
*Allow local overriding of the visbility of the Volume Utilization slider

v7.1 \/
*Update to KSP 0.24.2

v7.0 \/
*Add tanks to more FS parts
*Disable TweakScale on any part with a ModuleEngineConfigs / ModuleHybridEngine*
*TACLS now supports RF/MFT natively, so removed TACLS interaction cfg.
*Enable ElectricCharge in fuselages
*Update ElectricCharge utilization to be 500 rather than 100 (mass per EC unchanged). Now it quite closely matches Silver Zinc Oxide batteries in volume as well as mass.
*Spanier: add KSPX Short 2.5m RCS config.
*Revert to showing the full precision volume of tanks.
*Removed configs for B9 jets and rockets; use AJE.
*Removed last remaining engine configs (Starwaster's NTRs, TT Vector engine)--use an engine pack!
*By default bring back up the mass of solid fuel in a part to where it was pre-RF. Later configs will override.
*Temporarily disable the "dedicated" setting for tanks, it's just causing issues.
*Update to 0.24.1

v6.4 \/
*Allow fuselages to hold life support resources
*Allow CONFIGs to have techRequired items (can limit available Engine Configs based on R&D nodes researched)
*Fix service modules to not start full when TACLS/ECLSS is installed

v6.3 \/
*Add Roxette's Spaceplane Plus support
*Fix HGR so engines are not modified (done via engine config sets instead)
*Add RedAV8R's Kethane volume fixes
*Update ECLSS config; add TACLS config. Both should work correctly when their respective mods are present and not do anything when they're not.
*Made the refueling pump toggleable (in VAB/SPH and in flight)

v6.2 \/
*Added new fuels from RedAV8R
*Updated HGR patch from Sandworm
*Tweaked some Firespitter part volumes, added support for more fuselage parts
*swamp_ig:
+Fixed cloning
+Fixed default amounts not loading
+Fixed ProcParts interoperability issues
+Fixed compatibility with Engine Ignitor

v6.1 \/
The "NathanKell is away so swamp_ig is holding down the fort" release
*Fixed SRB issue
*Fixed phantom tanks
*Trimmed down the amount of guff being saved to the persistence files

v6   \/
*Updated plane parts (C7, Firespitter) to have B9-esque levels of resources. No more magic volume-disapppearing tricks when fuel tanks become fuselages.
*Massive improvements from swamp_ig, for integration with Procedural Parts and other mods, and UI improvements.
+Switchable tanks
+fixed symmetry bug
+(optional) tweakable utilization
+Better integration support
+loading fixes
+Display GUI from tweakables
+SI units
*taniwha: show version on GUI
*support more Firespitter parts
*Update to ModuleManager v2.1.5
*Sandworm: support HGR tanks

v5.3 \/
*Fix DLL
*Upgrade Kethane converter config to eadrom's.
*Fix ModuleRCSFX compatibility to be non-destructive
*Fix fixed launch clamp pumps (taniwha)
*Upgrade to ModuleManager v2.1.0

v5.2 \/
*Add support for Nazari's Mk3 expansion, add ECLSS fix, fix ARM patches.
*Fix launch clamps so they pump to all parts.
*ialdabaoth: add support for RF adjustments via tweakables.
*Fix RCS tank basemass
*Support ModuleRCSFX
*Fix some engine patches to play nicer with HotRockets

v5.1 \/
*Fixed RCS Sounds compatibility
*Fixed g0 constant in all RF-compatible engines to be the real 9.80665m/s rather than KSP's 9.82m/s (even though elsewhere they use 9.81, for engines they use 9.82).
*Fixed semi-automatic ModuleEnginesFX support to actually work.
*Preliminary tweakables support from swamp_ig
*Support new ARM tanks (taniwha)
*Support TurboNiso tanks (Spanier)
*Recompiled for .23.5
*Changed DLL name. YOU MUST DELETE OLD RF FOLDER BEFORE INSTALLING v5.1!

v5 = \/
*Moved RF-related Stretchy parts to RF.
*Add back missing HTP, N2O entries
*Speed improvements from Ferram (thanks!)
*ModuleEnginesFX support
*Firespitter tanks don't change basemass now.
*Fixed volume problems when rescaling StretchyTanks
*Fixed ModuleHybridEngine (used for, e.g. the bi/trimodal NTRs).
*Added Version Checker (Majiir, Ferram): RF will now warn you when run on a different version of KSP.
*Added Starwaster's fixes to NTRs and to the HeatPump
*taniwha: add support for resource/mass updates

v4.3 = \/
*Now engine heat dissipation and engine heat production are proportional to techlevel (in particular, heatProduction *= TLMassMultiplier and part.heatDissipation /= TLMassMultiplier.
*Fixed issues in loading code (when instantiating an engine).
*StretchyTanks will rescale upwards slightly better. Making them smaller still provokes rounding errors, however.
*Fixed big bug with useRealisticMass (mass multiplier was never used!).
*Non-SM, Non-RCS tanks can no longer hold monopropellant.
*Pressurized tanks (i.e. SM) now properly note they are pressurized.
*Fixed issue with TL0 Isp. It was too low (due to my having created techlevels before I added alcohol, and stupidly used alcohol/LOx Isp for TL0 kerolox Isp). This means, however, that upgrading TL0 engines no longer delivers quite the increase in thrust it used to.
*Lowered U+ TWR slightly to better accord with real turbopump-fed vacuum engines.
*Fixed sign error in ignitionsAvailable

v4.2 = \/
*Added fix to BobCat Soviet Engines until the original pack is updated.
*Added fix to prevent exception when RF module loads before the module it's controlling.
*Added fix to allow RF to work with RCS Sounds mod
*taniwha: fixed exception with prefabs; root parts can now be edited.
*updated to ModuleManager v1.5.6

v4.1 = \/
*Lowered aerospike sea-level Isp
*Added SNServiceModules
*Reworked thrust-setting code and fixed RCS thrust issue
*Now when you cut thrust, thrust is actually cut: min thrust goes to 0 for all engines (if the throttle is even 1% above 0, however, then minthrust will remain normal). This is so you can "cut off" engines without disabling them (i.e. by hitting x, or whatever your 0 throttle key is) and for MJ/KER/etc compatibility.
*Fixed duplicating fuel gauges
*Fixed EI ignitions remaining; added dictionary listing which propellants are pressurized.
*Added nitrogen (ArgonGas no longer modified, since this doesn't support the rest of NFPP yet)
*taniwha's MFT v4.1 fixes:
*Allow tank amounts to be set to 0. This fixes the problem with non-fillable tanks (eg, Kethane).
*Blank fields no longer reset to the previous amount and are treated as 0 when updating.
*Overfull tanks now display the available volume in red.
*Tanks placed using symmetry no longer invite the kraken (no more ship flying apart but still connected via fuel-lines)
*Source code removed from the distribution zip. See above links for details.


v4.0 = \/
*Rewritten Techlevels system. Supports per-part and per-config techlevel overrides.
*Techlevels can be keyed to technodes. Stock, TLs are tied to the Rocketry nodes (+ start for TL0)
*Rewritten MFT for .23 compatibility by taniwha (very cool!)
*Tons of small fixes for .23
*Utilization now works as pressure (in atmospheres) for gases.
*Added Starwaster's NTR rework
*Added EngineIgnitor integration
*Added throttling support
*Added list of propellants to ignore when autoconfiguring (in MFSSettings)

v3.4 = \/ (unreleased)
*Fixed basemass issue when using custom basemass (Thanks, RedAV8R!)
*Fixed Hybrid Engine techlevel support
*Native KSPI support

v3.3 = \/
*Swapped how thrust and mass are changed by TL increase.
*Made the battery multiplier (how much charge per unit of volume) configurable in MFSSettings
*Changed Xenon around. Tanks now hold 1/10 what they used to, and Xenon is now 10x as dense. It's kept pressurized at a hopefully reasonable temperature to yield that 0.2 g/cc density (what I've set it to).
*Changed tank masses again to try go get them ever closer in line with the real world.
*Added Balloon tanktype (practically no basemass; structural integrity kept by internal pressure). C.f. Atlas missile / LV. Same for BalloonCryo. Since the tank goes all the way to the skin they hold slightly more (at least the StretchyTank ones, the only ones so far, do).
*Fixed propellant ratios so that they are displayed in percents rather than 0.x ratios that get rounded.
*Added tooltips for hovering over autofill buttons to say which engines use that mixture.
*Added SRFirefox's fix for NTRs so nuclear fuel lasts longer. Also increased ElectricCharge generation.
*Added Syntin; changed Methane to LqdMethane to comport with other fuels.

v3.2 = \/
*Typo fix (thanks Starwaster!) to [un]fillable tanks.
*Increased precision for fuel densities; fixed LH2's density (was 0.071g/cc; should be 0.07085g/cc)
*Added tank support for Space Shuttle Engines mod (thanks, Malsheck!)
*Added scrolling to tank configuration.
*Upgraded to Module Manager v1.5 

v3.1 = \/
*Changed zip's path structure.
*Redid StretchyTanks support again. Get StretchySRB v5.
*Added new engines and tanks thanks to Chestburster's hard work
 -Added missing tanks from FASA
 -Added support for SDHI Service Module (take some Oxidizer and Liquid Fuel with you for the built-in FuelCell)
 -Added support for "new" Spherical Tanks
 -Added dtobi's Space Shuttle Engines
*Internal tweaks
*Added localCorrectThrust, in the ModuleEngineConfigs module. Set to false if MFS should not alter thrust due to Isp change (like for jets).
*Added useConfigAsTitle. Will change part title to config name when setting configuration, if true.
*Added new resources: UDMH (same as monopropellant but set to STACK_PRIORITY_SEARCH not ALL_VESSEL), Hydrazine, Aerozine, and Methane.

v3 === \/
*Finally put back stock masses and thrusts (well, in 99.9% of cases) for all engines. Some had to be tweaked ever-so-slightly as they were balance-breakers.
*Changed the Isp and TWR curves slightly for the tech levels
*added heatMultiplier setting to RealSettings.cfg. Change it to change the global heat multiplier for all ModularEngines.
*Fixed more typos
*Added missing tanks
*Redid the tank masses again for Realistic Masses mode. It should roughly correspond to S-IC for kerolox, Shuttle External Tank for cryogenic hydrolox, and Titan II-1 for hypergolic. In each case a slight additional mass was taken into account to stand for additional stage mass like fairings and decouplers; if you have a fuel tank alone, you'll get a better fuel fraction than real life stages. Note that since engines masses in MFSC incorporate thrust plate mass, tank mass will also be lower.
*Added enhanced StretchyTanks compatibility. Things should scale better now. Requires newest version of NathanKell's StretchySRB patch (unzip on top of StretchyTanks).

v3 Alpha=== \/
*Fixed some typos.
*Fixed symmetry bug (thanks Starwaster!)
*Fixed heat fin shutdown gui (thanks Starwaster!)
*Added support for real mode, where tanks and engines have real TWRs. For now we are using some temporary engine configs, so your engine performance may have changed. If you want to enable real mode, after extracting the RealFuels zip per instructions, open the ModularFuelTanks/RealFuels/RealSettings.cfg file and change the line useRealisticMass = false to useRealisticMass = true
(To support this, tank masses are slightly imprecise.)

v2 === \/
*Fixed problems in KW engine configs
*Added dynamic changing of tech levels
*Tweaked B9 SABRE Isp

v1a === \/
*Fixed typo in Liquid H2 density
*Fixed B9 SABRE air-breathing fuel:oxidizer (aka air) ratio. Was 1:6, should be 1:23.
*Forgot to add a note to the readme regarding Thermal Fins.

v1 === \/
*Initial release.
**New features for standard Modular Fuel Tanks**
*New from ialdabaoth's dev build: thermal fins, for cooling your parts.
*Bugifx to tank code so no longer is amount ignored when maxAmount is set by percent. amount now acts as a ratio of maxAmount if maxAmount is set by percent.
*Special handling for ElectricCharge. 1 unit of volume = 100 units of charge.
*New tank type: ServiceModule (includes fuel, monopropellant, and electric charge).
*New options for basemass. If basemass is -1, then all MFT mass calculations regarding tank mass are ignored; the part's mass will be left unchanged.
*Tank option overriding. When you add a ModuleFuelTanks module to a part, you can specify a custom basemass, which will override the TANKTYPE basemass. In addition, you can add TANK nodes, just like you were in a TANKTYPE node, and override the TANKTYPE's values. For example, if you want only oxidizer in your special tank by default, but want to leave the option for more, you can add a MFT module with tank type = Default, but then add a TANK{} node:
TANK
{
	name = LiquidFuel
	amount = 0
	maxAmount = 0
}
and it will override tank type Default's value for liquid fuel. Note that as yet you can't add tanks of resource type unsupported by that tank type.

**New features for RealFuels / Modular Engines**
*A complete rebalancing of nearly all engines to use real-world fuels at realistic specific impulses. Engines are classified by tech level and by type, and use appropriate Isps at sea level and in vacuum.
*Tech level can be changed in engine config, just like fuel mode.
*New from ialdabaoth's dev build: Hybrid Engines now work just like regular Modular Engines (i.e. they have CONFIGs), it's just you can switch them on the fly.
*New from ialdabaoth's dev build: thrust now, like in real life, scales with Isp. Fuel flow is constant, but thrust changes with air pressure. For this reason, lower-stage engines have been given a boost in thrust proportional to what they lost. Note that MechJeb, Kerbal Engineer Redux, and other thrust-showing mods will only show current TWR, and the VAB/SPH is considered vacuum. MechJeb now supports showing Sea Level TWR when a Modular Engine is attached to the vessel, and correctly calculates stage time and TWR during flight. KER support forthcoming.
*You may now have both a Modular Tanks AND a Modular Engines module in the same part, and both will work in the VAB/SPH. The ME display will be shifted to the right.
=======================
FUEL MIXTURES
====Chemical (Liquid Fuel)=====
*Kerosene aka RP-1) and LOx is the "standard" / "benchmark" mixture. It's what the early launchers used, and what many still do. It's non-toxic, quite dense, and the LOx is only mildly cryogenic, which means LOx boiloff is not a big issue except for weeks+ missions. For all these reasons, RP-1/LOx is the fuel of choice for lower stages.

*Liquid H2 and LOx is a high-performance mixture. It gives you more "bang for your buck" but it's less dense so you need more tanks (though the tanks are lighter per liter). However, engines burning LH2/LOx generally have a lower TWR than other engines (in MFS, 75%). Also, it is generally less efficient at sea level (proportionally) than other fuel mixtures. The main complication however is that LH2 must be kept extremely cold; boiloff is a serious issue even in insulated (cryogenic) tanks. You should always use cryogenic tanks with this mixture; they mass less for hydrolox than do regular tanks. Given these advantages and disadvantages, LH2/LOx is best for upper stages that are fired during the launch or a few days after; however, some lower-stage use has been done (Delta IV, Ariane 5, Shuttle / Ares / SLS) though often in combination with solid boosters.

*Liquid Methane and LOx is midway between RP-1/LOx and LH2/LOx: lower performance than hydrolox but denser, and less cryogenic.

*Various hypergolics. They are various nitrogen-based storable (but highly toxic) liquid fuels. They perform less well than RP-1/LOx (about 95% the specific impulse for modern hypergolics), but are slightly denser and non-cryogenic: they can be stored for months at a time. Given their advantage in density, in actual use they are better than 95% as efficient--tankage for them masses much less. Further, another key advantage is that they do not need ignition: hypergolic means that if the two substances are put in contact, they will burn with no outside trigger. N2O4 is the highest-performing oxidizer; MMH and Aerozine-50 are the highest-performing fuels (UDMH is more stable but has slightly worse performance). Other oxidizers (Nitric Acid, Nitrous Oxide) have lower performance, especially when used with a mixture of Amines rather than a hydrazine derivative like MMH, AZ50, or UDMH. N2O4 and MMH (or another fuel) is often used for high-performance bipropellant RCS (Gemini, Apollo, Shuttle, etc.).

*Kerosene and Hight-Test Peroxide is hypergolic when the HTP is heated and run over a catalyst bed. It's storable, non-toxic, very dense, but has lower performance than kerolox.

*Alcohol (75% Ethanol, 25% Water) and LOx was used before kerolox; it is worse in just about every respect compared to kerolox.

*Hydrazine, Nitrous Oxide, and HTP can be used as monopropellants. Hydrazine has by far the highest performance, but is very toxic.

*Solid Fuel is very dense, allows high thrust (since there is no engine per se to feed the fuel through, just a nozzle), but has low specific impulse, only slightly better than Hydrazine monopropellant. However, it took many years to develop larger and larger solid fuel motors, and solid fuel motors don't scale as well as liquid fuel (since the entire casing must resist the pressure of combustion, the casing itself forming the thrust chamber).

*RCS: Bipropellant RCS is the best, followed by Hydrazine, HTP, Nitrous Oxide, and Nitrogen gas. Bipropellant NTO/MMH is highly toxic, however, as is Hydrazine (from which MMH is derived). HTP is non-toxic and reasonably stable, as is Nitrous Oxide; Nitrogen gas has the worst performance (it is not catalyzed and decomposed, merely pressurized and shot through a nozzle).

====Nuclear (Liquid Fuel)====
In general: the less dense the fuel, the higher your specific impulse and the lower your thrust.

*Liquid Hydrogen is the benchmark. Highest performance, but least dense. Same drawbacks as hydrolox, above, except worse, because ALL the fuel is liquid hydrogen

*Liquid Methane has lower performance, but is much more dense and so, despite, the lower specific impulse, the lower tank mass often leads to higher total deltaV for your stage. Considerable increase in thrust.

*Liquid Ammonia has lower performance but also less of a thrust increase than Liquid Methane.

*LOx-Augmented NTR: This involves pumping LOx into the reactor along with LH2; thus you get a hybrid between a chemical rocket and a NTR. Massive thrust increase (8x or so) with a drop in efficiency down to about 65-70% what the NTR gets in pure-LH2 mode.

=======================
TANK TYPES
(Note: All tanks are pressurized, but some tanks are highly pressurized; you need the latter for pressure-fed engines.)

* Default: Your regular tank type, roughly equivalent to the tanks used for Titan or Saturn I/V. Note that in order to match Titan tanks, it's lighter than it should be for other cases. Features minimal insulation for cryogenic resources (equivalent to the insulation on Saturn V's S-IVB).

* Structural: A heavier version of Default, used for aircraft/spaceplanes (or to simulate old, massive tanks). Example: A jet plane's tank, or the R-7's.

* ServiceModule: Used for service modules and other pressure-fed tanks. You need this for pressure-fed engines (RCS are pressure-fed engines). Due to supporting up to 200atm of pressure rather for resources than just the 1.8 or so that non-highly-pressurized tanks do, it's much heavier. Also can store electricity and life support resources. Features more insulation for the cryogenic resources. Examples: Able/Delta upper stage, Apollo Service Module.

* Fuselage: A heavier version of ServiceModule, used for aircraft/spaceplanes (or to simulate old, massive tanks). Examples: the electronics and life support section of a spaceplane, the WAC Corporal sustainer.

* Cryogenic: Highly insulated tank. Has the level of insulation (and low boiloff) of a modern cryogenic tank. Slightly heavier than Default due to the increased insulation. Example: Delta IV Common Core Booster.

* Balloon: Light, fragile tank. In real life the tanks must be kept pressurized at all times (even when empty of propellants) or they will collapse, and if left unmonitored they can collapse. In addition they are much less strong than other tank types, and one cannot attach heavy things to their sides or place heavy masses above them under high acceleartion. Ingame, however, their only major disadvantage is that they cost more. Example: Atlas sustainer (pre-Atlas III).

* BalloonCryo: Same as Balloon, but with more insulation (to the level of type Cryogenic). Example: Centaur upper stage. The Shuttle ET falls in between this and type Cryogenic.

=========================
TECH LEVELS

In order to have realistic engine performance, RF divides engines into types and tech levels. While type cannot be changed in game (it is determined by chamber pressure, area ratio, cycle type, etc., all abstracted as "engine type" and applied, as well as can be done given how unrealistic most KSP engine models are, to most stock and mod engines), tech level can. Tech level represents the advances that are applied to a specific model of engine over time.
For example, the venerable LR87 used in the Titan rocket went through upwards of 11 revisions over time, and was converted to use all three main fuel types (kerolox, i.e. kerosene [RP-1] and Liquid Oxygen; hypergolic NTO / Aerozine-50; and hydrolox, i.e. Liquid Hydrogen and LOx). Its Isp and thrust-to-weight ratio increased considerably through the revisions.
Terms: SL = sea level, TWR = thrust to weight ratio. Note that in RF, all engines include the mass of their thrust plate, so RF TWRs will be about 20-30% lower than real life engine stats.

So, first RF classifies each engine it supports by type. The types are:

O = Orbital maneuvering system. Designed for tons and tons of restarts, and vacuum-only use. Pressure-Fed. High vac Isp, very poor SL isp, lowest TWR. Usually hypergolic. Real life examples: Apollo SPS, Shuttle OME. KSP example: LV-1

U = Upper stage. At most a couple restarts. Same Vac Isp as O, better SL Isp, better TWR. Real life example: the Titan's LR-91. KSP example: KR-2L Rhino.

U+ = Upper stage optimized for vac use. Aka "O with a turbopump." Highest Vac Isp (higher than O). Lower TWR than U. That's what + means, higher vac, lower SL, lower TWR. Real life examples: the Centaur's RL-10. KSP examples: LV-909, Poodle.

L = lower stage. No restarts. Lowest Vac Isp, highest SL Isp barring Aerospike, highest TWR. Real life examples: RD-170 and 180 on Zenit and Atlas V. Fuel is usually kerolox, though could be hypergolic or even hydrolox. KSP example: the Mainsail, obviously.

L+ = same changes as U+: higher IspV, lower IspSL, lower TWR. Designed for a single-stage-to-orbit stack (or at least an engine that's never staged away even if boosters are). Real life example: Space Shuttle SSME. Note that in real life most rockets have large lower stages and small upper stages, so most real life lower stage engines are somewhere between L and L+, if not outright L+. KSP example: LV-T45, Skipper.

A = Aerospike. Note that in real life nozzle losses are only 15% or so, and most of the efficiency loss at sea level is because there's air there, not because the nozzle is the wrong shape. For now they have the Isp of a U in vacuum, and 0.9x that at sea level. Real life examples: J-2T-250k (plug nozzle mod of the Saturn V J-2), the linear aerospike on the X-33. KSP Examples: obvious.

S = Solid.

S+ = Solid for vac use, lower SL, higher Vac Isp.

N = Nuclear Thermal. Approximately same ratio of IspV to IspSL as U. Uses a solid core reactor to heat reaction mass. Very low TWR, very high Isp. Real life examples: the various Project NERVA engines, RD-0410. KSP example: LV-N.

NOTE: RCS uses type 'L' for the Isp and TWR multipliers; in terms of actual performance it's like O.

What Isp an engine has is determind by grabbing its type, checking its tech level, and getting the appropriate entry in the TLTIsps area of RealSettings.cfg. Then any appropriate multipliers are applied (in the engine CONFIG).

Engines have minimum tech levels; they aren't available before that. You can, however, upgrade past that. In fact, you are HIGHLY encouraged to upgrade any engine, after placing it in the VAB, to your maximum available TL. Engines default to the lowest TL they can.

Rough TL to year table:
TL0: 1945-1955, WW2 and early rocketry.
TL1: 1957+, early Space Age rockets (straddles the divide between Redstone/Vanguard/R-5, and Atlas/R-7).
TL2: 1962+, Gemini and Saturn I (not IB), Voskhod/Molniya.
TL3: 1967+, Apollo
TL4: 1968+, Apollo Applications Program, N1, etc.
TL5: 1978+, Shuttle etc.
TL6: 1985+, the Shuttle era, 80s and 90s LVs.
Tl7: 2005+, the present day.

=========================
UPGRADE COSTS
It can cost funds or science to unlock an engine config and/or increase the tech level. Upgrade costs are controlled by global settings and by per-part settings. In general, it costs a fixed amount of funds and/or science to unlock an engine config, and it costs a fixed amount of funds and/or science, times (desired new tech level - original engine tech level) to unlock a new tech level (so going from TL3->4 is less expensive than 4->5 for a starts-at-TL3 engine).

CONFIG settings:
If a CONFIG has an entryCost, that will be its cost in funds to unlock. If it has a sciEntryCost, that will be its cost in science to unlock. If it has a techLevelEntryCost, that will be the base cost in funds to unlock a new TL (see above re the per-TL costs changing), and same for techLevelSciEntryCost. Per-config entry costs can also have multipliers and subtractors. This means if one CONFIG is very similar to another CONFIG (on that or another engine), you can get a reduction in cost if you've unlocked the other CONFIG. You can add a entryCostMultipliers {} node to the CONFIG and have a list of the other config(s)' names and their multipliers, like LR89-NA-6 = 0.5 (which will mean the current config costs only half as much to unlock if LR89-NA-6 is unlocked). This applies to both funds and science. You can do the same with the entryCostSubtractors {} node, except in this case, the value is subtracted from the final cost, rather than the final cost being multiplied by the value. In this case, the subtractive value will be multiplied by configCostToScienceMultiplier if the cost is a science cost (see below for configCostToScienceMultiplier).

Global settings (configs):
configEntryCostMultiplier: if no specific costs are set, then config entry cost will be (this * config.cost). So if the config has no extra cost (or a negative cost), the entrycost will be 0. Default 20.
configScienceCostMultiplier: Works like above for creating the science entry cost. Default 0.
configCostToScienceMultiplier: Used when subtracting from a config unlock science cost based on other unlocked configs. See above. Default 0.1.

Global settings (techlevels):
techLevelEntryCostFraction: The fixed cost in funds for unlocking a new techlevel for a CONFIG, if it is not specified in the CONFIG (see above), is based on the this times the sum of the config entry cost and the part's entry cost. Default 0.1.
techLevelScienceEntryCostFraction: The fixed cost in science for unlocking a new techlevel for a CONFIG, if it is not specified in the CONFIG (see above), is based on the this times the sum of the config's science entry cost and (the part's entry cost times configScienceCostMultiplier). Default 0.

usePartNameInConfigUnlock: If true, the part name will be prepended to the config name when checking for whether configs are unlocked. If you tend to use the same name for configs across multiple engines (i.e. both Mainsail and Poodle have `LqdOxygen+LqdHydrogen` configs) you want this true. If you are RO and each CONFIG represents a specific engine (and multiple parts may implement the same engine) you want this false. Default true (but RO sets it to false).

=========================
ULLAGE AND LIMITED IGNITIONS
RealFuels now integrates limited ignitions and support for ullage and pressure-fed engines.
* To start an engine, you must have the resources it requires to start, and you must have ignitions remaining.
* If ullage is enabled for the engine, and your propellant stability is not "Very Stable", there is a chance that vapor can get in the feed lines and the engine will flame out. You will need to set the throttle to 0 to reset things, then stabilize your propellants. Some forward RCS thrust, or thrust from ullage motors like small SRBs (solids and RCS aren't subject to ullage issues) will do that. Then you can try throttling the engine up again to restart it.
* If the engine is pressure-fed, it requires highly-pressurized tanks (see above for tank descriptions). It will not ignite and run without such tanks.

New parameters in ModuleEnginesRF:
ullage: Whether ullage simulation is enabled. Defaults to false.
pressureFed: Whether the engine is pressure-fed. Defaults to false.
ignitions: the number of ignitions the engine has. Defaults to -1 (unlimited).
IGNITOR_RESOURCE nodes: If you specify limited ignitions, an engine will consume these resources when it ignites, and will fail to ignite (but still use up an ignition) if they are not available. name defines the resource name (like ElectricCharge) and amount defines the amount required (just like EngineIgnitor).

Note that if you used to configure Engine Ignitor per-CONFIG in RF, those configs are still compatible. You only need to worry about the above parameters if you don't have ModuleEngineIgnitor {} nodes in you CONFIG nodes.


*********************
RealFuels contains code based on that of HoneyFox in EngineIgnitor. Original MIT license follows.
The MIT License (MIT)

Copyright (c) 2013 HoneyFox

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.