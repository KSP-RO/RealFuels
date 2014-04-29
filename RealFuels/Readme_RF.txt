**** RealFuels ****
by NathanKell
Contributors: Chestburster, Starwaster, taniwha, swamp_ig, ialdabaoth (obviously, but back again!)
ialdabaoth (who is awesome) created Modular Fuels, and this is a fork of the RealFuels branch.

License remains CC-BY-SA as modified by ialdabaoth.
Source: https://github.com/NathanKell/ModularFuelSystem (shared repository between RF and Modular Fuel Tanks).

Also included: Module Manager (by sarbian and ialdabaoth). See the Module Manager thread for details and license and source: http://http://forum.kerbalspaceprogram.com/threads/55219
Module Manager is required for RF to work.

DESCRIPTION:
Real Fuels does the following:
*It converts resources to use 1 unit = 1 liter at STP, and changes tank capacity to accord with that.
*It allows any supported tank to be filled with exactly how much or how little fuel you want, of whatever type you want (though different tanks may allow or disallow certain fuels; jet fuel tanks won't take oxidizer for instance). Tank dry masses are set to replicate real-world rocket stages.
*Real world fuels are added, and engines/RCS can use them, with realistic stats (NOTE: You NEED an engine pack in order for engines/RCS to use the new fuels).
*Engine/RCS thrust scales with Isp, just like in real life.
*Engines/RCS can have multiple configurations (for example, an upper stage could support a kerosene + liquid oxygen mode, and a liquid hydrogen + liquid oxygen mode). These modes have different thrust, Isp, etc.
*Engines/RCS can have varying techlevels: different performance characteristics for the same part, based on the techlevel you select (techlevels can be tied to R&D nodes for career games).
*Engines can have limited throttling, and (via installation of the Engine Ignitor mod by HoneyFox) can have limited ignitions.

INSTALL INSTRUCTIONS:
1. Delete any existing ModularFuelTanks folder or RealFuels folder in your KSP/GameData folder. This is VITAL.
2. Extract this archive to your KSP/GameData folder
3. Download and install an engine pack. This probably means grabbing Realism Overhaul, which is HIGHLY recommended for use with RF. Also, now that most RCS fuels do not flow everywhere but instead flow like LF/Ox, you might want CrossFeedEnabler for radial RCS tanks.

USAGE:
You can access RF-related GUIs ingame by tweakables, or by going to the Action Group Editor mode in the VAB/SPH (i.e. where you assign things to action groups) and clicking on a tank, engine, or RCS module. If supported, the GUI will appear.

For tanks:
At the top will appear the total tank mass (wet), the tank dry mass, the available, used, and total volume (in liters). Below appears the set of resources that may be added to the tank, and the current amounts and max amounts (if any).
If there are engines on the vessel, and available volume in the tank, autoconfigure buttons will appear at the bottom of the list, one for each fuel mixture used by the engines on the vessel. When you hover the cursor over an autoconfigure button, a tooltip will appear showing the engines that use that mixture. Click an autoconfigure button to automatically configure remaining volume for that mixture.
Note that gases and electric charge have multiple "units" per tank liter, since gases are given in liters at STP but stored under pressure, and electric charge is in KJ.

For engines/RCS:
At the top are buttons for changing the current engine's configuration. Then there are the buttons for changing techlevel. They will have X if a change in that direction is unavailable. Below that are stats for the current config and TL. NOTE that if your RCS uses a fuel that is set to STACK_PRIORITY_SEARCH rather than ALL_VESSEL (anything except MonoPropellant) you need to have fuel feeding your RCS thrusters (i.e. treat them like radial engines).


AN OVERVIEW OF FUEL TYPES AND TECH LEVELS/ENGINE TYPES ARE BELOW THE CHANGELOG

==========
Changelog:
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


=========================
TECH LEVELS

In order to have realistic engine performance, RF divides engines into types and tech levels. While type cannot be changed in game (it is determined by chamber pressure, area ratio, cycle type, etc., all abstracted as "engine type" and applied, as well as can be done given how unrealistic most KSP engine models are, to most stock and mod engines), tech level can. Tech level represents the advances that are applied to a specific model of engine over time.
For example, the venerable LR87 used in the Titan rocket went through upwards of 11 revisions over time, and was converted to use all three main fuel types (kerolox, i.e. kerosene [RP-1] and Liquid Oxygen; hypergolic NTO / Aerozine-50; and hydrolox, i.e. Liquid Hydrogen and LOx). Its Isp and thrust-to-weight ratio increased considerably through the revisions.
Terms: SL = sea level, TWR = thrust to weight ratio. Note that in RF, all engines include the mass of their thrust plate, so RF TWRs will be about 20-30% lower than real life engine stats.

So, first RF classifies each engine it supports by type. The types are:

O = Orbital maneuvering system. Designed for tons and tons of restarts, and vacuum-only use. Pressure-Fed. High vac Isp, very poor SL isp, lowest TWR. Usually hypergolic. Real life examples: Apollo SPS, Shuttle OME. KSP examples: LV-1, LV-909, Poodle.

U = Upper stage. At most a couple restarts. Same Vac Isp as O, better SL Isp, better TWR. Real life example: the Titan's LR-91. KSP doesn't really have any of these, they'd be somewhere between the LV-909 and the LV-T45. Given the way it's shaped, I made the Skipper one, for example.

U+ = Upper stage optimized for vac use. Aka "O with a turbopump." Highest Vac Isp (higher than O). Lower TWR than U. That's what + means, higher vac, lower SL, lower TWR. Real life examples: the Centaur's RL-10. KSP doesn't have any examples, but KW's Apollo-SPS lookalike performs like one (though it is properly made type O by me, given what the real SPS was). Hydrolox is the fuel mode of choice.

L = lower stage. No restarts. Lowest Vac Isp, highest SL Isp barring Aerospike, highest TWR. Real life examples: RD-170 and 180 on Zenit and Atlas V. KSP example: the Mainsail, obviously. (Although with its Isp unchanged, it's really an L+, so that's how I rate it.) Fuel is usually kerolox, though could be hypergolic or even hydrolox.

L+ = same changes as U+: higher IspV, lower IspSL, lower TWR. Designed for a single-stage-to-orbit stack (or at least an engine that's never staged away even if boosters are). Real life example: Space Shuttle SSME. KSP example: LV-T45. Note that in real life most rockets have large lower stages and small upper stages, so most real life lower stage engines are somewhere between L and L+, if not outright L+.

A = Aerospike. Note that in real life nozzle losses are only 15% or so, and most of the efficiency loss at sea level is because there's air there, not because the nozzle is the wrong shape. For now they have the Isp of a U in vacuum, and 0.9x that at sea level. Real life examples: J-2T-250k (plug nozzle mod of the Saturn V J-2), the linear aerospike on the X-33. KSP Examples: obvious.

S = Solid.

S+ = Solid for vac use, lower SL, higher Vac Isp.

N = Nuclear Thermal. Approximately same ratio of IspV to IspSL as U. Uses a solid core reactor to heat reaction mass. Very low TWR, very high Isp. Real life examples: the various Project NERVA engines, RD-0410. KSP example: LV-N.

What Isp an engine has is determind by grabbing its type, checking its tech level, and getting the appropriate entry in the TLTIsps area of RealSettings.cfg. Then any appropriate multipliers are applied (in the engine CONFIG).

Engines have minimum tech levels; they aren't available before that. You can, however, upgrade past that. In fact, you are HIGHLY encouraged to upgrade any engine, after placing it in the VAB, to your maximum available TL. Engines default to the lowest TL they can.

Rough TL to year table:
TL0: 1945+, WW2 and immediate postwar
TL1: 1955+, early Space Age rockets (Redstone, R-5, Vanguard)
TL2: 1960+, Mercury, Vostok (Sputnik is halfway between TL1 and TL2)
TL3: 1963+, Gemini, early Apollo stuff
TL4: 1968+, Late Apollo
TL5: 1973+, Apollo Applications Program, etc.
TL6: 1980+, the Shuttle era
Tl7: 1995+, the present day.